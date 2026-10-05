using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InfantGod.Core.CGSystem;
using Spine.Unity;
using UnityEngine;

namespace Graywill.InfantGodCodex
{
    public sealed class CGViewer : IDisposable
    {
        private GameObject root;
        private Camera camera;
        private RenderTexture target;
        private Texture2D preview;
        private bool renderedFrame;
        private readonly List<SkeletonAnimation> skeletons = new List<SkeletonAnimation>();
        private int pendingFrames;
        private float baseSize = 1;
        private string[] previewFiles = new string[0];
        public int PreviewFrame { get; private set; }
        public int PreviewFrameCount => previewFiles.Length;
        public float Zoom = 1;
        public bool Paused;
        public string Id { get; private set; }
        public string Status { get; private set; } = "选择一张 CG。";
        public string[] Animations { get; private set; } = new string[0];
        public string CurrentAnimation { get; private set; }
        public Texture Texture => target != null ? (Texture)target : preview;
        public bool IsReady => preview != null || (target != null && renderedFrame);

        public void Open(CGEntry entry, string dataPath)
        {
            Dispose();
            Id = entry.id;
            Zoom = 1;
            Paused = false;
            CGDefinition definition = CGDefinitionRegistry.Get(entry.id);
            if (definition == null || definition.TargetSpinePrefab == null)
            {
                string file = string.IsNullOrEmpty(entry.preview) ? null : Path.Combine(dataPath, entry.preview);
                if (file != null && File.Exists(file))
                {
                    previewFiles = Directory.GetFiles(Path.GetDirectoryName(file), entry.id + "_*.png").OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    if (previewFiles.Length == 0) previewFiles = new[] { file };
                    ShowFrame(0);
                    Status = "静态镜头。";
                }
                else Status = "暂时没有可显示的画面。";
                return;
            }

            // 先放入非活动根节点，移除会控制游戏的脚本后再激活。
            root = new GameObject("GraywillCodex_CG_Isolated");
            root.SetActive(false);
            root.transform.position = new Vector3(10000, 10000, 0);
            GameObject clone = UnityEngine.Object.Instantiate(definition.TargetSpinePrefab, root.transform, false);
            clone.name = "Preview_" + entry.id;
            foreach (Behaviour behaviour in clone.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour is SkeletonRenderer) continue;
                if (behaviour is MonoBehaviour) UnityEngine.Object.DestroyImmediate(behaviour);
                else behaviour.enabled = false;
            }
            foreach (Component component in clone.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                string name = component.GetType().Name;
                if (name.StartsWith("Rigidbody", StringComparison.Ordinal) || name.StartsWith("Collider", StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(component);
            }
            clone.SetActive(true);

            // 与游戏的 CharacterRenderImage 使用同一相机预制体，保留专用渲染器配置。
            // CG 也保留原图层；任意改到 Layer 31 会被游戏的渲染层筛选排除。
            var cameraPrefab = Resources.Load<GameObject>("RenderTexture/CharacterRenderCamera");
            if (cameraPrefab == null) throw new InvalidOperationException("缺少游戏的 CG 预览相机资源。");
            var cameraObject = UnityEngine.Object.Instantiate(cameraPrefab, root.transform, false);
            cameraObject.name = "GraywillCodex_CG_Camera";
            // 原作像素相机会按主游戏分辨率覆盖取景大小；独立查看器自行匹配 CG 边界。
            foreach (var component in cameraObject.GetComponents<MonoBehaviour>())
                if (component.GetType().Name == "PixelPerfectCamera") UnityEngine.Object.DestroyImmediate(component);
            camera = cameraObject.GetComponent<Camera>();
            if (camera == null) throw new InvalidOperationException("CG 预览相机没有 Camera 组件。");
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(35, 39, 39, 255);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100;
            camera.aspect = 16f / 9f;
            target = new RenderTexture(1024, 576, 24, RenderTextureFormat.ARGB32);
            target.filterMode = FilterMode.Point;
            target.antiAliasing = 1;
            target.Create();
            camera.targetTexture = target;
            root.SetActive(true);
            skeletons.AddRange(clone.GetComponentsInChildren<SkeletonAnimation>(true));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var animation in skeletons)
            {
                animation.Initialize(false);
                if (animation.Skeleton == null) continue;
                foreach (var item in animation.Skeleton.Data.Animations) names.Add(item.Name);
            }
            Animations = names.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (Animations.Length != 0) Play(Animations[0]);
            pendingFrames = 2;
            Status = "画面准备中。";
        }

        public void Tick()
        {
            if (root == null || camera == null) return;
            foreach (var animation in skeletons) animation.timeScale = Paused ? 0 : 1;
            if (pendingFrames > 0)
            {
                pendingFrames--;
                if (pendingFrames != 0) return;
                var renderers = root.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Status = "无法显示这张 CG。";
                    return;
                }
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                baseSize = Mathf.Max(0.1f, Mathf.Max(bounds.extents.y, bounds.extents.x / camera.aspect) * 1.06f);
                camera.transform.position = bounds.center + new Vector3(0, 0, -20);
                camera.transform.rotation = Quaternion.identity;
                Status = "动画预览。";
            }
            camera.orthographicSize = baseSize / Mathf.Max(Zoom, 0.1f);
            camera.Render();
            renderedFrame = true;
        }

        public void Play(string name)
        {
            CurrentAnimation = name;
            foreach (var animation in skeletons)
                if (animation.Skeleton != null && animation.Skeleton.Data.FindAnimation(name) != null)
                    animation.AnimationState.SetAnimation(0, name, true);
        }

        public void ShowFrame(int index)
        {
            if (previewFiles.Length == 0) return;
            PreviewFrame = (index + previewFiles.Length) % previewFiles.Length;
            if (preview != null) UnityEngine.Object.Destroy(preview);
            preview = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(preview, File.ReadAllBytes(previewFiles[PreviewFrame]));
            preview.filterMode = FilterMode.Point;
        }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            if (target != null)
            {
                if (camera != null) camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
            if (preview != null) UnityEngine.Object.Destroy(preview);
            root = null;
            camera = null;
            target = null;
            preview = null;
            skeletons.Clear();
            Animations = new string[0];
            CurrentAnimation = null;
            previewFiles = new string[0];
            PreviewFrame = 0;
            Id = null;
            pendingFrames = 0;
            renderedFrame = false;
        }
    }
}
