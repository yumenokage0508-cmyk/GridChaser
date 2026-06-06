#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
GridChaser 一笔画关卡生成器 v5  (纯标准库, 无需安装依赖)

方法: 路径优先(先走再定地图)
  1) 在地图内部预埋若干障碍当"柱子"
  2) 绕着柱子走一条自回避随机路径 —— 这条轨迹本身就是保证可解的一笔画解
  3) 走过的格子=可玩区, 没走到的格子=墙(含内部柱子), 路径起止点=S/G
  4) 用 Warnsdorff 贪心试玩估【人类难度】, 按难度+结构(柱子/隧道)区间筛选
  优点: 可解性由构造保证(不用慢速DFS), 天然带柱子/隧道/多腔感, 产量高。

规则(与游戏一致): 从S出发, 每个非墙格走且仅走一次, 终点G必须最后到达。
全部关卡 requireAllVisited=true, 无敌人。
用法(Windows): python level_generator.py

想跑哪几组: 改文件下方的 JOBS 那一行 (注释/取消注释切换), 三组定义本身永远保留。

输出:
  - generated_<组名>.txt   人类可读, 方便肉眼扫一遍
  - generated_<组名>.json  供 Unity 导入器读取; 每个文件带本批完整参数(出处记录),
                           知道"是哪组设置产出了这批关卡", 便于策展阶段对比批次。
"""
import random, sys, time, re, json
from collections import deque
from datetime import datetime

# ============ 关卡批次定义 (三组都留着, 不删) ============
# 字段说明:
#   greedy: 行走贪心度。低→更多隧道、更难; 高→更开阔、更易。
#   n_pillars: 预埋柱子数。
#   min_pillars / min_chokepoints: 输出必须达到的结构下限。
#   min/max_difficulty: 难度 = 1 - 贪心通关率, 0 最易 1 最难。
#   candidates: 尝试候选数 (越大越慢但命中越多)。 want: 该组最终输出关数。

JOB_中等 = {
    "name":"组1_中等",
    "width":11,"height":11,"greedy":0.80,"n_pillars":7,
    "min_cells":28,"max_cells":46,"min_pillars":1,"min_chokepoints":6,
    "min_difficulty":0.45,"max_difficulty":0.78,
    "rollouts":150,"candidates":2500,"want":8,"seed":None,
}

JOB_偏难 = {
    "name":"组2_偏难",
    "width":12,"height":12,"greedy":0.68,"n_pillars":8,
    "min_cells":34,"max_cells":58,"min_pillars":2,"min_chokepoints":10,
    "min_difficulty":0.70,"max_difficulty":0.95,
    "rollouts":150,"candidates":3000,"want":8,"seed":None,
}

JOB_强隧道 = {
    "name":"组3_强隧道",
    "width":13,"height":13,"greedy":0.58,"n_pillars":8,
    "min_cells":34,"max_cells":60,"min_pillars":2,"min_chokepoints":14,
    "min_difficulty":0.60,"max_difficulty":0.95,
    "rollouts":150,"candidates":6000,"want":8,"seed":None,
}

# 冒烟测试: 仅用来快速验证"输出格式/管线是否通", 几秒出 3 关。
# 不要拿它的关卡当正式内容 (地图小、难度没卡)。seed 固定, 每次结果一样。
JOB_冒烟测试 = {
    "name":"smoke_test",
    "width":7,"height":7,"greedy":0.80,"n_pillars":3,
    "min_cells":10,"max_cells":40,"min_pillars":0,"min_chokepoints":0,
    "min_difficulty":0.0,"max_difficulty":1.0,
    "rollouts":30,"candidates":400,"want":3,"seed":42,
}

# ====== 想跑哪些, 就在这里列哪些 (改这一行, 注释/取消注释切换) ======
JOBS = [JOB_中等, JOB_偏难, JOB_强隧道]      # 默认: 三组全跑
# JOBS = [JOB_中等]                          # 只跑组1
# JOBS = [JOB_偏难, JOB_强隧道]              # 只跑较难的两组
# JOBS = [JOB_冒烟测试]                       # 只验证输出格式 (秒出, 不是正式内容)
# ===================================================================

DIRS=[(0,1),(0,-1),(-1,0),(1,0)]
DELTA2CH={(0,1):'U',(0,-1):'D',(-1,0):'L',(1,0):'R'}
M_MOVE={'U':(0,1),'D':(0,-1),'L':(-1,0),'R':(1,0)}

# ---------- 路径优先生成 ----------
def gen_pathfirst(W,H,greedy,n_pillars,rng):
    blocked=set(); tries=0
    while len(blocked)<n_pillars and tries<80:
        tries+=1; p=(rng.randint(2,W-3),rng.randint(2,H-3))
        if all(abs(p[0]-q[0])+abs(p[1]-q[1])>=2 for q in blocked): blocked.add(p)
    start=None
    while start is None:
        c=(rng.randrange(W),rng.randrange(H)); start=c if c not in blocked else None
    vis={start}; path=[start]; pos=start
    def free(c):
        return 0<=c[0]<W and 0<=c[1]<H and c not in vis and c not in blocked
    while True:
        nb=[(pos[0]+dx,pos[1]+dy) for dx,dy in DIRS if free((pos[0]+dx,pos[1]+dy))]
        if not nb: break
        if rng.random()<greedy:
            on=lambda c: sum(1 for dx,dy in DIRS if free((c[0]+dx,c[1]+dy)))
            m=min(on(c) for c in nb); nxt=rng.choice([c for c in nb if on(c)==m])
        else: nxt=rng.choice(nb)
        vis.add(nxt); path.append(nxt); pos=nxt
    return set(vis),path,blocked

def interior_pillars(cells,blocked):
    return sum(1 for b in blocked if all((b[0]+dx,b[1]+dy) in cells for dx,dy in DIRS))

# ---------- 工具 ----------
def neighbors_map(cells):
    return {c:[(c[0]+dx,c[1]+dy) for dx,dy in DIRS if (c[0]+dx,c[1]+dy) in cells] for c in cells}
def chokepoints(cells,nbr): return sum(1 for c in cells if len(nbr[c])<=2)

def rollout(cells,S,G,nbr,rng):
    N=len(cells); pos=S; visited={S}
    while len(visited)<N:
        cand=[nb for nb in nbr[pos] if nb not in visited and not(nb==G and len(visited)!=N-1)]
        if not cand: return False
        on=lambda nb: sum(1 for x in nbr[nb] if x not in visited)
        m=min(on(nb) for nb in cand)
        pos=rng.choice([nb for nb in cand if on(nb)==m]); visited.add(pos)
    return pos==G

def path_to_moves(p):
    return "".join(DELTA2CH[(p[i][0]-p[i-1][0],p[i][1]-p[i-1][1])] for i in range(1,len(p)))
def verify(cells,S,G,moves):
    pos=S; vis={S}
    for ch in moves:
        dx,dy=M_MOVE[ch]; nxt=(pos[0]+dx,pos[1]+dy)
        if nxt not in cells or nxt in vis: return False
        if nxt==G and len(vis)!=len(cells)-1: return False
        pos=nxt; vis.add(nxt)
    return pos==G and len(vis)==len(cells)
def to_layout(cells,S,G):
    xs=[x for x,y in cells]; ys=[y for x,y in cells]
    return ["".join('S' if (x,y)==S else 'G' if (x,y)==G else '.' if (x,y) in cells else 'X'
            for x in range(min(xs),max(xs)+1)) for y in range(max(ys),min(ys)-1,-1)]

# ---------- 单组任务 ----------
def run_job(cfg):
    rng=random.Random(cfg["seed"]); W,H=cfg["width"],cfg["height"]
    results=[]; tried=0; t0=time.time()
    print(f"\n========== {cfg['name']} 开始 ==========")
    while tried<cfg["candidates"]:
        tried+=1
        if tried%50==0:
            sys.stdout.write(f"\r  尝试 {tried}/{cfg['candidates']}  命中 {len(results)}  用时 {time.time()-t0:.0f}s   ")
            sys.stdout.flush()
        cells,path,blk=gen_pathfirst(W,H,cfg["greedy"],cfg["n_pillars"],rng)
        if not(cfg["min_cells"]<=len(cells)<=cfg["max_cells"]): continue
        nbr=neighbors_map(cells); S,G=path[0],path[-1]
        ip=interior_pillars(cells,blk); ck=chokepoints(cells,nbr)
        if ip<cfg["min_pillars"] or ck<cfg["min_chokepoints"]: continue
        wins=sum(1 for _ in range(cfg["rollouts"]) if rollout(cells,S,G,nbr,rng))
        diff=1.0-wins/cfg["rollouts"]
        if not(cfg["min_difficulty"]<=diff<=cfg["max_difficulty"]): continue
        moves=path_to_moves(path)
        if not verify(cells,S,G,moves): continue          # 保险: 独立重放校验
        results.append((diff,len(cells),ip,ck,to_layout(cells,S,G),moves))
        if len(results)>=cfg["want"]*3: break
    results.sort(key=lambda r:(-r[0],-r[1])); out=results[:cfg["want"]]
    print(f"\r  完成: 尝试 {tried}, 命中 {len(results)}, 输出 {len(out)}, 用时 {time.time()-t0:.0f}s        ")

    safe=re.sub(r'[^A-Za-z0-9_]','',cfg['name'])

    # ---- 输出 1: 人类可读 txt（与原来一致） ----
    fn=f"generated_{safe}.txt"; lines=[]
    for i,(diff,n,ip,ck,layout,moves) in enumerate(out,1):
        head=f"==== {cfg['name']} 关卡{i}  格数={n}  难度={diff:.0%}  内部柱子={ip}  咽喉格={ck} ===="
        print(head); lines.append(head)
        for row in layout: print("  "+row); lines.append(row)
        print(f"  solutionMoves = {moves}\n"); lines.append(f"solutionMoves={moves}\n")
    open(fn,"w",encoding="utf-8").write("\n".join(lines))
    print(f"  -> 已写入 {fn}")

    # ---- 输出 2: JSON（供 Unity 导入器读取, 带本批参数作为出处记录） ----
    # layout 用 '\n' 拼成单字符串, 行序"顶部在前", 正好对上 GridManager.layout.Split('\n') 的读法。
    json_levels=[{
        "name":          f"{safe}_L{i}",
        "layout":        "\n".join(layout),
        "solutionMoves": moves,
        "requireAllVisited": True,
        "difficulty":    round(diff,4),
        "cells":         n,
        "interiorPillars": ip,
        "chokepoints":   ck,
    } for i,(diff,n,ip,ck,layout,moves) in enumerate(out,1)]
    payload={
        "job":         cfg,                                   # 本批完整参数, 出处记录
        "generatedAt": datetime.now().isoformat(timespec="seconds"),
        "levels":      json_levels,
    }
    json_fn=f"generated_{safe}.json"
    with open(json_fn,"w",encoding="utf-8") as f:
        json.dump(payload,f,ensure_ascii=False,indent=2)
    print(f"  -> 已写入 {json_fn}")

    return len(out)

def main():
    total=sum(run_job(cfg) for cfg in JOBS)
    print(f"\n===== 全部完成, 共输出 {total} 关 =====")

if __name__=="__main__":
    main()
