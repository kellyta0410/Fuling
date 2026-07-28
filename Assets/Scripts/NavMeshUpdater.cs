using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;

public class NavMeshUpdater : MonoBehaviour
{
    [Header("防抖与更新设置")]
    [Tooltip("延迟更新时间（秒），在此期间内的多次更新请求会被合并为一次")]
    public float updateDelay = 0.5f;

    private NavMeshSurface surface;
    private bool pendingUpdate = false;
    private bool isBaking = false;
    private Coroutine updateCoroutine;

    void Start()
    {
        surface = GetComponent<NavMeshSurface>();
        if (surface == null) surface = FindObjectOfType<NavMeshSurface>();

        if (surface != null)
        {
            // 游戏启动时，同步烘焙初版 NavMesh，生成基础的数据结构
            surface.BuildNavMesh();
            Debug.Log("✅ NavMesh 初始烘焙完成");
        }
        else
        {
            Debug.LogError("❌ NavMeshUpdater: 场景中未找到 NavMeshSurface 组件！");
        }
    }

    /// <summary>
    /// 外部调用的触发接口
    /// </summary>
    public void RequestUpdate()
    {
        if (surface == null) return;

        pendingUpdate = true;

        // 如果没有正处于等待队列的协程，则启动一个
        if (updateCoroutine == null)
        {
            updateCoroutine = StartCoroutine(ProcessUpdateDelay());
        }
    }

    private IEnumerator ProcessUpdateDelay()
    {
        // 1. 等待防抖延迟，合并这段时间内玩家移动触发的所有请求
        yield return new WaitForSeconds(updateDelay);

        // 2. 如果上一次异步烘焙还没结束，等待其完成
        while (isBaking)
        {
            yield return null;
        }

        // 3. 执行异步网格更新
        if (pendingUpdate && surface != null && surface.navMeshData != null)
        {
            pendingUpdate = false;
            isBaking = true;

            // 核心：异步更新 NavMesh，完全不阻塞主线程渲染
            AsyncOperation asyncOp = surface.UpdateNavMesh(surface.navMeshData);

            while (!asyncOp.isDone)
            {
                yield return null;
            }

            isBaking = false;
            Debug.Log("🔄 NavMesh 异步更新完成");
        }

        updateCoroutine = null;

        // 4. 如果在异步烘焙期间又有新 Tile 生成/销毁，再次触发更新
        if (pendingUpdate)
        {
            RequestUpdate();
        }
    }
}