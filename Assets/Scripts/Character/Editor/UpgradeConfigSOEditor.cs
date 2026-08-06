using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(UpgradeConfigSO))]
public class UpgradeConfigSOEditor : Editor
{
    SerializedProperty configName;
    SerializedProperty maxLevel;
    SerializedProperty costBase;
    SerializedProperty costIncrease;
    SerializedProperty costAcceleration;
    SerializedProperty manualLevels;

    void OnEnable()
    {
        configName = serializedObject.FindProperty("configName");
        maxLevel = serializedObject.FindProperty("maxLevel");
        costBase = serializedObject.FindProperty("costBase");
        costIncrease = serializedObject.FindProperty("costIncrease");
        costAcceleration = serializedObject.FindProperty("costAcceleration");
        manualLevels = serializedObject.FindProperty("manualLevels");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var cfg = (UpgradeConfigSO)target;

        EditorGUILayout.PropertyField(configName);
        EditorGUILayout.PropertyField(maxLevel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("金币花费（自动规律计算）", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(costBase);
        EditorGUILayout.PropertyField(costIncrease);
        EditorGUILayout.PropertyField(costAcceleration);

        EditorGUILayout.HelpBox("金币由上方规律自动计算，逐级列表里每项的 Cost 请填 0。", MessageType.Info);

        // 费用预览
        EditorGUILayout.LabelField("费用预览：", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        for (int lv = 2; lv < cfg.maxLevel; lv++)
        {
            EditorGUILayout.LabelField($"Lv.{lv} → Lv.{lv + 1}：{cfg.GetLevelCost(lv)} 金币");
        }
        EditorGUILayout.LabelField($"Lv.{cfg.maxLevel}：MAX（免费）");
        EditorGUI.indentLevel--;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("手动逐级加成（每个等级一行）", EditorStyles.boldLabel);
        if (manualLevels.arraySize == 0)
            EditorGUILayout.HelpBox("列表为空时升级仍然生效，但面板不会显示加成描述，请至少填 1 条。", MessageType.Info);
        EditorGUILayout.PropertyField(manualLevels, true);

        serializedObject.ApplyModifiedProperties();
    }
}