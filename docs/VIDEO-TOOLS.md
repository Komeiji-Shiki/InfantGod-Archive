# 适合由编程助手操作的视频工具

这份比较针对“让 AI 代理完成真实素材的录制、剪辑、字幕和配乐编排”，不是生成虚构的视频画面。资料核对于 2026-10-05。

| 工具 | 适合承担的工作 | 代理怎样操作 |
| --- | --- | --- |
| FFmpeg | 截取和拼接实录、转场、缩放、字幕、音频混合、响度处理、编码导出 | 命令行参数与滤镜脚本，容易保存完整制作过程并重复生成 |
| Remotion | 用代码精确编排标题、注释、画中画、字幕和镜头时间线 | React/TypeScript 组成视频，官方提供针对编程代理的制作与渲染文档 |
| MLT / melt | 多轨剪辑、滤镜、合成与可复用时间线 | 命令行和 XML 工程，可与使用 MLT 的剪辑工具结合 |
| Shotcut | 人工接手修改同一类时间线、预览和细调 | 图形界面与 MLT 工程；需要可视交互时可以操作 UI |

本项目的宣传片采用实际 Mod 画面、可复现的镜头与字幕编排，以及 FFmpeg 导出。片中的功能演示来自真实运行界面。

## 官方资料

- [FFmpeg 滤镜文档](https://ffmpeg.org/ffmpeg-filters.html)：音频、视频滤镜、时间线与字幕相关参数。
- [FFmpeg 采集设备](https://ffmpeg.org/ffmpeg-devices.html#gdigrab)：Windows 桌面／窗口采集。
- [Remotion：与编程代理制作视频](https://www.remotion.dev/docs/ai/coding-agents)。
- [Remotion 官方代理技能](https://www.remotion.dev/docs/ai/skills)：构图、声音、字幕、工作室和渲染。
- [MLT melt 文档](https://www.mltframework.org/docs/melt/)。
- [Shotcut 官方代码仓库](https://github.com/mltframework/shotcut)。

