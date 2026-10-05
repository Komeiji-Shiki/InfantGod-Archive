# 宣传片素材

成片采用实际运行的资料终端界面。原生 Mod 与网页分别取景，画面上保留对应功能名称。

- `captures/`：实际界面素材。
- `compose_music.py`：原创配乐《Terminal at Dawn》的完整合成程序，108 BPM。
- `terminal-at-dawn.wav`：原创配乐，立体声 44.1 kHz。
- `render_video.py`：剪辑、标题、中文字幕与导出脚本。
- `trailer.srt`：可另行修改的中文字幕。

依赖：Python 3、NumPy、Pillow、fontTools，以及带 H.264/AAC 编码器的 FFmpeg。重新合成配乐后，运行剪辑脚本即可导出视频。

配乐与制作代码采用 MIT；包含《幼神》原作画面的截图和成片按 CC BY-NC-SA 4.0 分享。原作作者 colsalley。
