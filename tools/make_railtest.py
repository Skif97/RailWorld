# Текстура для перевірки, яку частину текстури бере кожна грань рейки:
# 4x4 клітинки по 32 px (у моделі рейки текстура 32 одиниці, клітинка 8 одиниць, тобто чверть).
# Колір клітинки за стовпчиком (u), яскравість за рядком (v), у клітинці номер 1..16 (рядок*4 + стовпчик + 1).
import zlib, struct, sys
N = 128; C = 32
FONT = {
 '0':["111","101","101","101","111"], '1':["010","110","010","010","111"],
 '2':["111","001","111","100","111"], '3':["111","001","111","001","111"],
 '4':["101","101","111","001","001"], '5':["111","100","111","001","111"],
 '6':["111","100","111","101","111"], '7':["111","001","001","001","001"],
 '8':["111","101","111","101","111"], '9':["111","101","111","001","111"],
}
HUES = [(220,40,40),(40,180,40),(40,80,230),(230,200,30)]   # стовпчики: червоний, зелений, синій, жовтий
SHADE = [1.0, 0.75, 0.55, 0.35]                              # рядки: від світлого до темного
px = [[(0,0,0)]*N for _ in range(N)]
for row in range(4):
    for col in range(4):
        r,g,b = HUES[col]; k = SHADE[row]
        colr = (int(r*k), int(g*k), int(b*k))
        for y in range(row*C, row*C+C):
            for x in range(col*C, col*C+C):
                edge = (x % C in (0, C-1)) or (y % C in (0, C-1))
                px[y][x] = (0,0,0) if edge else colr
        text = str(row*4 + col + 1); s = 4
        w = len(text)*3*s + (len(text)-1)*s
        x0 = col*C + (C - w)//2; y0 = row*C + (C - 5*s)//2
        for i,ch in enumerate(text):
            for fy,line in enumerate(FONT[ch]):
                for fx,bit in enumerate(line):
                    if bit != '1': continue
                    for dy in range(s):
                        for dx in range(s):
                            px[y0+fy*s+dy][x0+i*4*s+fx*s+dx] = (255,255,255)
raw = b"".join(b"\x00" + bytes(c for p in line for c in p) for line in px)
def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t+d) & 0xffffffff)
png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", N, N, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
open(sys.argv[1], "wb").write(png)
print("ok")
#
# Як користуватися: python tools/make_railtest.py tools/testtextures/railtest.png, покласти railtest.png у
# RailWorld/assets/RailWorld/textures/block/ і в itemtypes/rail.json тимчасово замінити текстуру
# "rusty-iron2" на { "base": "railworld:block/railtest" }. Після перевірки повернути як було.
