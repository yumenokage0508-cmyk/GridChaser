using UnityEngine;

// 配色主题（数据资产）。一套完整配色 = 一个 .asset 文件。
// 可建多个（Dark / HighContrast / Warm…），拖到 GridManager 的 Theme 字段上即可整套切换对比。
// Play 模式下改本资产的颜色会永久保存（不像场景/组件改动会被还原），适合边跑边调。
[CreateAssetMenu(menuName = "Game/ColorTheme")]
public class ColorTheme : ScriptableObject
{
    [Header("场景")]
    public Color background = new Color(0.102f, 0.102f, 0.102f);   // #1A1A1A

    [Header("格子")]
    public Color normal = new Color(0.165f, 0.165f, 0.165f);     // #2A2A2A 可走格
    public Color visited = new Color(0.361f, 0.329f, 0.569f);     // #5C5491 踩过(紫)
    public Color goal = new Color(0.961f, 0.784f, 0.259f);     // #F5C842 终点(金)
    public Color wall = new Color(0.290f, 0.290f, 0.322f);     // #4A4A52 墙(已提对比度)

    [Header("角色")]
    public Color player = new Color(0.494f, 0.812f, 1f);         // #7ECFFF 玩家(浅蓝)
}