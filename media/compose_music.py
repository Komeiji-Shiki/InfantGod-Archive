"""原创配乐《Terminal at Dawn》：柔和的电子音色、像素琶音与轻鼓组。

运行：python media/compose_music.py
依赖：numpy。旋律、和声与合成器参数均保存在这里，音频可重新生成。
"""
from pathlib import Path
import math,wave
import numpy as np

ROOT=Path(__file__).resolve().parent
RATE=44100
BPM=108
BEAT=60/BPM
DURATION=32*4*BEAT+1.5
audio=np.zeros((math.ceil(DURATION*RATE),2),dtype=np.float64)
rng=np.random.default_rng(8105)

def add(at,signal,gain=1,pan=0):
    start=round(at*RATE)
    signal=signal[:max(0,len(audio)-start)]*gain
    audio[start:start+len(signal),0]+=signal*math.sqrt((1-pan)/2)
    audio[start:start+len(signal),1]+=signal*math.sqrt((1+pan)/2)

def tone(note,length,kind='bell'):
    t=np.arange(round(length*RATE))/RATE
    f=440*2**((note-69)/12)
    phase=2*np.pi*f*t
    if kind=='bass':
        sound=np.sin(phase)+.18*np.sin(phase*2)
        envelope=np.minimum(t/.016,1)*np.exp(-t*3)*np.minimum((length-t)/.09,1)
    elif kind=='pad':
        sound=(np.sin(phase)+.5*np.sin(phase*1.002)+.18*np.sin(phase*2))/1.68
        envelope=np.minimum(t/.16,1)*np.minimum((length-t)/.35,1)
    else:
        sound=np.sin(phase+.003*np.sin(2*np.pi*5*t))+.24*np.sin(phase*2)+.1*np.sin(phase*3)
        envelope=np.minimum(t/.006,1)*np.exp(-t*(4 if kind=='arp' else 2.7))*np.minimum((length-t)/.04,1)
    return sound*np.clip(envelope,0,1)

# 每组四小节依次为 Am9、Fmaj7、Cmaj7、G6；旋律在后半段上移八度。
chords=[[57,60,64,71],[53,57,60,64],[48,55,59,64],[55,59,62,69]]
melodies=[[76,72,71,69,72,76,79,76],[76,72,69,67,69,72,76,72],[74,76,79,76,71,67,64,67],[71,74,76,74,69,67,64,67]]
for bar in range(32):
    time=bar*4*BEAT
    chord=chords[(bar//2)%4]
    section=bar//8
    # 片头和结尾收轻，中段加入低音和旋律。
    intensity=0.7 if bar<2 or bar>=30 else 1
    for index,note in enumerate(chord):
        add(time,tone(note+12,4*BEAT+.25,'pad'),.075*intensity,(index-1.5)/3)
    for beat in [0,1.5,2,3.5]:
        add(time+beat*BEAT,tone(chord[0]-12,.44,'bass'),.27*intensity,0)
    for step in range(8):
        note=chord[[0,2,1,3,2,1,3,2][step]]+24
        add(time+step*BEAT/2,tone(note,.38,'arp'),.09*intensity,(-.45 if step%2 else .45))
    if 2<=bar<30:
        notes=melodies[(bar//2)%4]
        for beat,note in zip([0,.75,1.5,2.5],notes[(bar%2)*4:(bar%2+1)*4]):
            if section==2 and bar%4==3:note+=12
            melody=tone(note,.7)
            add(time+beat*BEAT,melody,.17,-.08)
            add(time+(beat+.75)*BEAT,melody,.038,.42)
    if 4<=bar<30:
        for beat in [0,2]:
            t=np.arange(round(.26*RATE))/RATE
            kick=np.sin(2*np.pi*(45*t+6*(1-np.exp(-t*30))))*np.exp(-t*20)
            add(time+beat*BEAT,kick,.32)
        for beat in [1,3]:
            t=np.arange(round(.15*RATE))/RATE
            noise=rng.normal(0,1,len(t));noise=np.convolve(noise,np.ones(4)/4,'same')
            snare=(noise*.42+np.sin(2*np.pi*175*t)*.3)*np.exp(-t*30)
            add(time+beat*BEAT,snare,.18,-.12)
        for beat in [.5,1.5,2.5,3.5]:
            t=np.arange(round(.06*RATE))/RATE
            noise=rng.normal(0,1,len(t));noise=np.diff(noise,prepend=0)*.45
            add(time+beat*BEAT,noise*np.exp(-t*70),.04,.32)

# 收尾淡出与柔和限幅，避免点击声和突兀截断。
fadein=np.minimum(np.arange(len(audio))/RATE/.75,1)
fadeout=np.minimum((DURATION-np.arange(len(audio))/RATE)/3.2,1)
audio*=np.minimum(fadein,fadeout)[:,None]
audio=np.tanh(audio*.9)
peak=np.max(np.abs(audio));audio*=.78/max(peak,1e-8)
target=ROOT/'terminal-at-dawn.wav'
with wave.open(str(target),'wb') as f:
    f.setnchannels(2);f.setsampwidth(2);f.setframerate(RATE)
    f.writeframes((audio*32767).astype('<i2').tobytes())
print(f'原创配乐已生成：{target.name}，{DURATION:.2f} 秒，峰值 {20*np.log10(np.max(np.abs(audio))):.1f} dBFS')
