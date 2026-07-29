using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BuffType
{
    SpeedUp,
    PowerUp,
    Heal
}

[CreateAssetMenu(fileName = "Buff_New", menuName = "Game/Buff Data")]
public class BuffDataSO : ScriptableObject
{
    public BuffType buffType;
    public string buffName;
    public Sprite icon;

    [Header("效果类型")]
    public bool isInstantEffect; // true=即时（Heal），false=持续（SpeedUp/PowerUp）

    [Header("持续Buff参数（仅当 isInstantEffect = false 时有效）")]
    public float duration = 5f;
    public int maxStack = 1;

    [Header("Heal 专用参数")]
    public bool isFullRestore;   // 是否回满血
    public float effectValue;    // 回血量 / 速度增量 / 攻击增量

    [Header("场景模型")]
    public GameObject pickupPrefab;
}