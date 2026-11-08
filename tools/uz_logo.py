import math

# Логотип УЗ у координатах зображення 1280 x 457 (y униз). Чи точка всередині білої фігури
def ellipse_x(cx, cy, a, b, y):
    t = (y - cy) / b
    return cx + a * math.sqrt(1 - t * t) if abs(t) <= 1 else -1e9

def semi(cx, cy, r, y):
    d = r * r - (y - cy) ** 2
    return cx + math.sqrt(d) if d > 0 else cx

def lerp_edge(points, y):
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        if y0 <= y <= y1:
            return x0 + (x1 - x0) * (y - y0) / (y1 - y0)
    return None

def inside(x, y):
    # Поза рамкою зображення фігури немає (рівняння нижче там не діють)
    if x < 0 or y < 0 or x > 1280 or y > 457:
        return False
    # Ліва нога У
    if y <= 355 and 0.465 * y <= x <= 130 + 0.479 * y:
        return True
    # Похилий штрих У і З праворуч від нього
    main_left = 337 - 0.479 * y
    if x >= main_left:
        outer = max(ellipse_x(500, 125, 180, 125, y), ellipse_x(500, 330, 182, 127, y), 500 if y <= 1 else -1e9)
        if x <= outer:
            channel_left = 463.5 - 0.474 * y
            if y >= 113 and x > channel_left:
                if y <= 182:
                    right = semi(495, 147.5, 34.5, y)
                elif y < 277:
                    right = 544.3 - 0.474 * y
                elif y <= 345:
                    right = semi(500, 311, 34, y)
                else:
                    right = 544.3 - 0.474 * y
                if x < right:
                    return False
            return True
    # Верхній брусок
    if 0 <= y <= 195:
        left = lerp_edge([(693, 0), (735, 80), (740, 120), (723, 195)], y)
        right = 957 + 148 * math.sqrt(max(0.0, 1 - ((88 - y) / 88) ** 2)) if y <= 88 else 1105 + 52 * (y - 88) / 107
        if left <= x <= right:
            return True
    # Нижній брусок
    if 263 <= y <= 457:
        left = lerp_edge([(727, 263), (745, 345), (740, 400), (695, 457)], y)
        right = 1188 + 92 * (y - 263) / 194
        if left <= x <= right:
            return True
    return False

def grid(rows):
    cell = 457 / rows
    cols = int(round(1280 / cell))
    out = []
    for r in range(rows):
        y = (r + 0.5) * cell
        out.append("".join("X" if inside((c + 0.5) * cell, y) else "." for c in range(cols)))
    return out

if __name__ == "__main__":
    for row in grid(18):
        print(row)


# ── Фігури на вагоні ──
# Літери УЗ без брусків логотипа: бруски стають смугами вздовж вагона з косими зрізами на кінцях
LETTERS_END = 690
TOP_BAR = (0, 195)       # смуги по висоті, y зображення, як бруски логотипа
BOTTOM_BAR = (263, 457)

def letters(x, y):
    return x <= LETTERS_END and inside(x, y)

def convex(points):
    """Опукла фігура з вершин за годинниковою стрілкою чи проти неї: функція «чи точка всередині»."""
    def fn(x, y):
        sign = 0
        for (ax, ay), (bx, by) in zip(points, points[1:] + points[:1]):
            c = (bx - ax) * (y - ay) - (by - ay) * (x - ax)
            if abs(c) < 1e-9:
                continue
            if sign == 0:
                sign = 1 if c > 0 else -1
            elif (c > 0) != (sign > 0):
                return False
        return True
    return fn

# Кінці смуг. Початок біля літер: верхня зрізана вниз праворуч, нижня вниз ліворуч, обидві огинають талію З.
# Кінець біля другого торця: похилий зріз, як у брусків логотипа. STRIPE_START і STRIPE_FINISH це межі, за якими
# смуга вже пряма і йде прямокутником
STRIPE_START, STRIPE_FINISH = 800, 1000
TOP_START = [(705, 0), (STRIPE_START, 0), (STRIPE_START, 195), (740, 195)]
BOTTOM_START = [(745, 263), (STRIPE_START, 263), (STRIPE_START, 457), (705, 457)]
TOP_FINISH = [(STRIPE_FINISH, 0), (1065, 0), (1157, 195), (STRIPE_FINISH, 195)]
BOTTOM_FINISH = [(STRIPE_FINISH, 263), (1188, 263), (1280, 457), (STRIPE_FINISH, 457)]

# ── Покриття фігури прямокутниками, що лежать на її контурі ──
MARGIN = 1.5                     # запас від межі, пікселі: прямокутник не вилазить за край між точками перевірки
INNER_ANGLES = (0.0, 25.4, -25.4)  # повороти внутрішніх прямокутників: прямо і вздовж штрихів У

def inner_candidates(fn, x0, x1, angles, cell=6.0, min_cells=4):
    """Найбільші прямокутники всередині фігури, повернуті на angles: для кишень, куди не дістає жоден прямокутник
    від контуру. У тому самому вигляді (p, q, n, t): p-q нижній бік у повернутих осях, n угору в них."""
    out = []
    for deg in angles:
        c, s_ = math.cos(math.radians(deg)), math.sin(math.radians(deg))
        def back(a, b):
            return a * c - b * s_, a * s_ + b * c
        corners = [(x, y) for x in (x0, x1) for y in (0, 457)]
        a_list = [x * c + y * s_ for x, y in corners]
        b_list = [-x * s_ + y * c for x, y in corners]
        amin, bmin = min(a_list), min(b_list)
        na, nb = int((max(a_list) - amin) / cell) + 1, int((max(b_list) - bmin) / cell) + 1
        node = [[fn(*back(amin + i * cell, bmin + j * cell)) for j in range(nb + 1)] for i in range(na + 1)]
        good = [[node[i][j] and node[i + 1][j] and node[i][j + 1] and node[i + 1][j + 1] for j in range(nb)] for i in range(na)]
        heights = [0] * na
        seen = set()
        for j in range(nb):
            for i in range(na):
                heights[i] = heights[i] + 1 if good[i][j] else 0
            stack = []
            for i in range(na + 1):
                h = heights[i] if i < na else 0
                start = i
                while stack and stack[-1][1] >= h:
                    si, sh = stack.pop()
                    if (i - si) * sh >= min_cells and (si, i, sh, j) not in seen:
                        seen.add((si, i, sh, j))
                        a0, a1 = amin + si * cell, amin + i * cell
                        b0, b1 = bmin + (j - sh + 1) * cell, bmin + (j + 1) * cell
                        p, q = back(a0, b0), back(a1, b0)
                        out.append((p, q, (-s_, c), b1 - b0))
                    start = si
                if h > 0 and (not stack or stack[-1][1] < h):
                    stack.append((start, h))
    return out
def boundary_loops(fn, x0, x1, step=2.0):
    """Межа фігури ламаними: ребра між клітинками сітки, зібрані в замкнені петлі."""
    cols, rows = int((x1 - x0) / step) + 3, int(457 / step) + 3
    grid = [[fn(x0 + (i - 0.5) * step, (j - 0.5) * step) for j in range(rows)] for i in range(cols)]
    def g(i, j):
        return 0 <= i < cols and 0 <= j < rows and grid[i][j]
    nxt = {}
    for i in range(cols):
        for j in range(rows):
            if not grid[i][j]:
                continue
            if not g(i, j - 1): nxt[(i, j)] = (i + 1, j)
            if not g(i + 1, j): nxt[(i + 1, j)] = (i + 1, j + 1)
            if not g(i, j + 1): nxt[(i + 1, j + 1)] = (i, j + 1)
            if not g(i - 1, j): nxt[(i, j + 1)] = (i, j)
    loops = []
    while nxt:
        start = next(iter(nxt))
        loop = [start]
        p = nxt.pop(start)
        while p != start:
            loop.append(p)
            p = nxt.pop(p)
        loops.append([(x0 + (x - 1) * step, (y - 1) * step) for x, y in loop])
    return loops

def simplify(points, tolerance):
    """Дуглас — Пекер для замкненої ламаної."""
    def rdp(pts):
        if len(pts) < 3:
            return pts
        (ax, ay), (bx, by) = pts[0], pts[-1]
        dx, dy = bx - ax, by - ay
        length = math.hypot(dx, dy) or 1e-9
        worst, index = 0, 0
        for k in range(1, len(pts) - 1):
            d = abs((pts[k][0] - ax) * dy - (pts[k][1] - ay) * dx) / length
            if d > worst:
                worst, index = d, k
        if worst <= tolerance:
            return [pts[0], pts[-1]]
        return rdp(pts[:index + 1])[:-1] + rdp(pts[index:])
    far = max(range(len(points)), key=lambda k: math.dist(points[0], points[k]))
    return rdp(points[:far + 1])[:-1] + rdp(points[far:] + [points[0]])[:-1]

def segments(fn, x0, x1, tolerance=3.0):
    """Відрізки межі: (p, q, n), n одинична нормаль у бік фігури."""
    out = []
    for loop in boundary_loops(fn, x0, x1):
        pts = simplify(loop, tolerance)
        for k in range(len(pts)):
            p, q = pts[k], pts[(k + 1) % len(pts)]
            dx, dy = q[0] - p[0], q[1] - p[1]
            length = math.hypot(dx, dy)
            if length < 1e-6:
                continue
            n = (-dy / length, dx / length)
            mx, my = (p[0] + q[0]) / 2, (p[1] + q[1]) / 2
            if not fn(mx + n[0] * 3, my + n[1] * 3):
                n = (-n[0], -n[1])
            out.append((p, q, n))
    return out

def rectangle_inside(fn, p, q, n, t, step=1.5):
    """Чи прямокутник від відрізка p-q завглибшки t у бік n цілком у фігурі (перевірка точками)."""
    length = math.dist(p, q)
    da, db = max(2, int(length / step) + 1), max(2, int(t / step) + 1)
    for i in range(da + 1):
        a = 0.01 + 0.98 * i / da
        bx, by = p[0] + (q[0] - p[0]) * a, p[1] + (q[1] - p[1]) * a
        for j in range(1, db + 1):
            d = t * j / db
            if not fn(bx + n[0] * d, by + n[1] * d):
                return False
    return True

def deepest(fn, p, q, n, limit=457.0):
    """Найбільша глибина, на яку прямокутник від відрізка заходить у фігуру, не виходячи з неї."""
    lo, hi = 0.0, limit
    if rectangle_inside(fn, p, q, n, hi):
        return hi
    for _ in range(14):
        mid = (lo + hi) / 2
        if rectangle_inside(fn, p, q, n, mid):
            lo = mid
        else:
            hi = mid
    return lo

def cover(fn, x0, x1, splits=8, sample=4.0, goal=0.995, tolerance=3.0, extra=0):
    """Мало прямокутників, що лежать рівно на контурі фігури: кандидати це підвідрізки кожного відрізка межі,
    витягнуті всередину на найбільшу глибину; жадібно береться той, що закриває найбільше ще не закритих точок.
    Повертає ([(p, q, n, t)], частка покритих точок)."""
    import heapq
    pts = [(x, y) for x in [x0 + sample / 2 + k * sample for k in range(int((x1 - x0) / sample))]
                  for y in [sample / 2 + k * sample for k in range(int(457 / sample))] if fn(x, y)]
    candidates = []
    for p, q, n in segments(fn, x0, x1, tolerance):
        m = splits if math.dist(p, q) > 40 else 1
        for i in range(m):
            for j in range(i + 1, m + 1):
                pa = (p[0] + (q[0] - p[0]) * i / m, p[1] + (q[1] - p[1]) * i / m)
                pb = (p[0] + (q[0] - p[0]) * j / m, p[1] + (q[1] - p[1]) * j / m)
                t = deepest(fn, pa, pb, n) - MARGIN
                if t > 2:
                    candidates.append((pa, pb, n, t))
    contour_count = len(candidates)
    if extra:
        candidates += inner_candidates(fn, x0, x1, INNER_ANGLES)
    def covers(rect):
        p, q, n, t = rect
        dx, dy = q[0] - p[0], q[1] - p[1]
        length = math.hypot(dx, dy)
        return frozenset(k for k, (x, y) in enumerate(pts)
                         if -0.5 <= ((x - p[0]) * dx + (y - p[1]) * dy) / length <= length + 0.5
                         and -0.5 <= (x - p[0]) * n[0] + (y - p[1]) * n[1] <= t + 0.5)
    sets = [covers(r) for r in candidates]
    uncovered = set(range(len(pts)))
    target = (1 - goal) * len(pts)
    chosen = []
    def pick(pool, enough):
        """Жадібно з кандидатів pool, доки enough() не скаже досить."""
        nonlocal uncovered
        heap = [(-len(sets[k]), k) for k in pool]
        heapq.heapify(heap)
        while not enough() and heap:
            neg, k = heapq.heappop(heap)
            gain = len(sets[k] & uncovered)
            if gain == 0:
                continue
            if heap and -heap[0][0] > gain:
                heapq.heappush(heap, (-gain, k))
                continue
            chosen.append(candidates[k])
            uncovered = uncovered - sets[k]
    # Спершу лише прямокутники від контуру, до goal; тоді ще extra найкращих з усіх, разом із внутрішніми
    pick(range(contour_count), lambda: len(uncovered) <= target)
    base = len(chosen)
    pick(range(len(candidates)), lambda: len(chosen) >= base + extra)
    return chosen, 1 - len(uncovered) / max(1, len(pts))
