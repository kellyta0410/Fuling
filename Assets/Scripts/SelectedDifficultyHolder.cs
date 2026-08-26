using UnityEngine;

// 跨场景保存菜单选中的难度引用。
// GameManager 不是持久单例（切场景会新建实例），PlayerPrefs 只能存字符串，
// 所以用一个静态字段把 DifficultySettings 引用原样带到地牢场景的 GameManager。
public static class SelectedDifficultyHolder
{
    public static DifficultySettings Current;
}
