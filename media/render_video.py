"""用真实界面素材制作 1080p 宣传片；镜头、字幕和标题均可编辑。

运行：python media/render_video.py --ffmpeg C:/ffmpeg/bin/ffmpeg.exe
依赖：Pillow、fontTools。配乐先由 compose_music.py 生成。
"""
from pathlib import Path
import argparse,json,math,subprocess,shutil
from PIL import Image,ImageDraw,ImageFont
from fontTools.ttLib import TTFont

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--ffmpeg',default='ffmpeg')
parser.add_argument('--output',type=Path)
parser.add_argument('--cards-only',action='store_true',help='只生成片头、片尾与封面')
args=parser.parse_args()
ROOT=Path(__file__).resolve().parent
REPO=ROOT.parent
WORK=ROOT/'.render'
WORK.mkdir(exist_ok=True)
WIDTH,HEIGHT,FPS=1920,1080,30
BEAT=60/108
PAPER='#eeebc6';INK='#28312f';ORANGE='#fb8b35';MUTED='#afb99e'

font=TTFont(REPO/'assets/fonts/fusion-pixel.woff2');font.flavor=None;font.save(WORK/'pixel.ttf')
pixel=lambda size:ImageFont.truetype(str(WORK/'pixel.ttf'),size)
system=Path('C:/Windows/Fonts/msyh.ttc')
body=lambda size:ImageFont.truetype(str(system if system.exists() else WORK/'pixel.ttf'),size)

def draw_card(path,ending=False):
    im=Image.new('RGB',(WIDTH,HEIGHT),INK);d=ImageDraw.Draw(im)
    for x in range(24,WIDTH,24):
        for y in range(24,HEIGHT,24):d.rectangle((x,y,x+1,y+1),fill='#465244')
    d.rectangle((96,110,1824,950),outline=PAPER,width=3)
    d.rectangle((96,110,1824,168),fill=PAPER)
    d.text((124,126),'OMNIARCHIVE.EXE  //  INFANT GOD',font=pixel(25),fill=INK)
    d.text((136,238),'爱式塔 · 技术 / 资料已就绪',font=pixel(32),fill=ORANGE)
    d.text((136,334),'幼神资料终端',font=pixel(108),fill=PAPER)
    d.text((144,488),'剧情查询  /  分支浏览  /  CG 收藏',font=pixel(38),fill=PAPER)
    d.rectangle((144,586,840,590),fill=ORANGE)
    if ending:
        d.text((144,650),'源码与下载',font=body(30),fill=MUTED)
        d.text((144,715),'github.com/Komeiji-Shiki',font=pixel(40),fill=PAPER)
        d.text((144,780),'/InfantGod-Archive',font=pixel(46),fill=ORANGE)
    else:
        d.text((144,650),'监督，资料已经整理好了。',font=body(42),fill=PAPER)
        d.text((144,739),'游戏内终端 + 完整离线资料页',font=pixel(31),fill=MUTED)
        d.text((144,818),'WINDOWS  /  DEMO  /  v1.0',font=pixel(24),fill=ORANGE)
    avatar=Image.open(REPO/'assets/portraits/Aistalt_Tech.png').convert('RGBA')
    avatar=avatar.resize((520,520),Image.Resampling.NEAREST)
    d.rectangle((1188,295,1736,843),fill='#515e53',outline='#b8c0a1',width=3)
    im.paste(avatar,(1202,309),avatar)
    d.text((1190,862),'TECH  /  知识应该方便查阅。',font=pixel(23),fill=MUTED)
    if ending:d.text((144,910),'原作《幼神》 / colsalley    Mod / Komeiji-Shiki · Graywill',font=pixel(21),fill=MUTED)
    im.save(path)

def overlay(path,number,heading,subtitle):
    im=Image.new('RGBA',(WIDTH,HEIGHT),(0,0,0,0));d=ImageDraw.Draw(im)
    d.rectangle((0,0,WIDTH,110),fill=INK)
    d.rectangle((0,HEIGHT-115,WIDTH,HEIGHT),fill=INK)
    d.rectangle((48,30,112,84),fill=ORANGE)
    d.text((63,40),f'{number:02}',font=pixel(30),fill=INK)
    d.text((137,34),heading,font=pixel(40),fill=PAPER)
    d.text((1510,45),'OMNIARCHIVE',font=pixel(25),fill=MUTED)
    d.rectangle((48,HEIGHT-82,54,HEIGHT-39),fill=ORANGE)
    d.text((75,HEIGHT-83),subtitle,font=body(32),fill=PAPER)
    im.save(path)

def run(command):
    subprocess.run([args.ffmpeg,'-hide_banner','-loglevel','error','-y',*map(str,command)],check=True,cwd=ROOT)

# 每个镜头采用真实界面图或实录。视频仅使用轻微缩放与直角标题，不重绘功能界面。
scenes=[
 {'asset':'title.png','beats':8,'title':'','caption':''},
 {'asset':'native-desktop.mp4','beats':16,'title':'游戏桌面的资料终端','caption':'点击桌面快捷方式，或按 F8 打开。'},
 {'asset':'native-search.png','beats':16,'title':'从角色、选项或一句台词开始','caption':'剧情、人格发言、协议与记忆，放在同一处查阅。'},
 {'asset':'web-branches-close.png','beats':12,'title':'沿着真正的分支阅读','caption':'选择、条件与后续，顺着连线查看。'},
 {'asset':'web-follow.png','beats':12,'title':'点击后续，继续阅读','caption':'进入下一段对话，保留完整的上下文。'},
 {'asset':'web-tech-dialogue.png','beats':16,'title':'四种人格，各自的发言','caption':'共情、技术、键政、媚俗，可以分别查询。'},
 {'asset':'web-dice.png','beats':16,'title':'查清每个选项的条件','caption':'话题等级、属性与检定，放在一起查看。'},
 {'asset':'native-cg.mp4','beats':16,'title':'CG 画廊','caption':'在游戏内独立查看画面，切换镜头与动画。'},
 {'asset':'native-mode.png','beats':8,'title':'查看范围，由你选择','caption':'想保留未知，就选择“仅已探索”。'},
 {'asset':'end.png','beats':8,'title':'','caption':''},
]
draw_card(WORK/'title.png');draw_card(WORK/'end.png',True)
if args.cards_only:
    shutil.copy2(WORK/'title.png',ROOT/'cover.png')
    print('片头、片尾与封面已生成。')
    raise SystemExit(0)
subtitle_rows=[];cursor=0;clips=[]
for i,scene in enumerate(scenes):
    duration=scene['beats']*BEAT
    source=(WORK if scene['asset'] in ['title.png','end.png'] else ROOT/'captures')/scene['asset']
    if not source.exists():raise SystemExit('缺少真实界面素材：'+str(source))
    is_video=source.suffix.lower() in ('.mp4','.webm','.mov')
    overlay_path=WORK/f'overlay-{i:02}.png'
    target=WORK/f'clip-{i:02}.mp4'
    inputs=['-stream_loop','-1','-i',source] if is_video else ['-loop','1','-framerate',FPS,'-i',source]
    if scene['title']:
        overlay(overlay_path,i,scene['title'],scene['caption'])
        inputs+=['-loop','1','-framerate',FPS,'-i',overlay_path]
        # 保持实际界面比例；上下标题区让字幕与游戏正文互不遮挡。
        filt='[0:v]scale=1836:830:force_original_aspect_ratio=decrease:flags=lanczos,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=0x28312f,setsar=1[base];[base][1:v]overlay=0:0,format=yuv420p[v]'
    else:
        filt='[0:v]scale=1920:1080:flags=lanczos,setsar=1,format=yuv420p[v]'
    run(inputs+['-filter_complex',filt,'-map','[v]','-an','-t',f'{duration:.6f}','-r',FPS,'-c:v','libx264','-preset','medium','-crf','18','-pix_fmt','yuv420p',target])
    clips.append(target)
    if scene['caption']:subtitle_rows.append((cursor+.2,cursor+duration-.2,scene['caption']))
    cursor+=duration
    print(f'镜头 {i+1}/{len(scenes)} 已导出：{scene["asset"]}',flush=True)

def stamp(seconds):
    ms=round(seconds*1000);h,ms=divmod(ms,3600000);m,ms=divmod(ms,60000);s,ms=divmod(ms,1000)
    return f'{h:02}:{m:02}:{s:02},{ms:03}'

(ROOT/'trailer.srt').write_text('\n\n'.join(f'{i+1}\n{stamp(a)} --> {stamp(b)}\n{text}' for i,(a,b,text) in enumerate(subtitle_rows))+'\n',encoding='utf8')
(ROOT/'storyboard.json').write_text(json.dumps({'fps':FPS,'width':WIDTH,'height':HEIGHT,'duration':cursor,'scenes':scenes},ensure_ascii=False,indent=2),encoding='utf8')
concat=WORK/'concat.txt';concat.write_text('\n'.join("file '"+p.as_posix()+"'" for p in clips),encoding='utf8')
target=args.output or ROOT/'InfantGod-Archive-Trailer.mp4'
run(['-f','concat','-safe','0','-i',concat,'-i',ROOT/'terminal-at-dawn.wav','-map','0:v:0','-map','1:a:0','-vf','format=yuv420p','-c:v','libx264','-preset','medium','-crf','18','-r',FPS,'-af',f'loudnorm=I=-18:TP=-1.5:LRA=8,afade=t=out:st={cursor-2.3:.3f}:d=2.3','-c:a','aac','-b:a','192k','-ar','48000','-t',f'{cursor:.6f}','-movflags','+faststart',target])
shutil.copy2(WORK/'title.png',ROOT/'cover.png')
print(f'宣传片已生成：{target}，{cursor:.2f} 秒',flush=True)
