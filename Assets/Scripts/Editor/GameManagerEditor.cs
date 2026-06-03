using UnityEngine;
using UnityEditor;
using System.Linq;

[CustomEditor(typeof(GameManager))]
public class GameManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 画原有的默认 Inspector 字段
        DrawDefaultInspector();

        EditorGUILayout.Space(10);

        if (GUILayout.Button("自动填充关卡列表", GUILayout.Height(30)))
        {
            AutoFillLevels();
        }
    }

    private void AutoFillLevels()
    {
        // 扫描 Assets/Levels/ 下所有 LevelData 资产
        string[] guids = AssetDatabase.FindAssets("t:LevelData", new[] { "Assets/Levels" });

        if (guids.Length == 0)
        {
            Debug.LogWarning("Assets/Levels/ 下没有找到任何 LevelData 文件！");
            return;
        }

        // 按文件名排序，保证 Level_01 < Level_02 < Level_10
        var levelDatas = guids
            .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
            .OrderBy(path => path)
            .Select(path => AssetDatabase.LoadAssetAtPath<LevelData>(path))
            .ToArray();

        // 写入 SerializedProperty，让 Undo 系统也能追踪这次改动
        SerializedProperty levelsProp = serializedObject.FindProperty("levels");
        levelsProp.arraySize = levelDatas.Length;

        for (int i = 0; i < levelDatas.Length; i++)
        {
            levelsProp.GetArrayElementAtIndex(i).objectReferenceValue = levelDatas[i];
        }

        serializedObject.ApplyModifiedProperties();

        Debug.Log($"已自动填充 {levelDatas.Length} 个关卡：{string.Join(", ", levelDatas.Select(l => l.name))}");
    }
}