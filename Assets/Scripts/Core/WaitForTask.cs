using System.Threading.Tasks;
using UnityEngine;

// 코루틴에서 Task가 끝날 때까지 기다린다.
//
// 바깥 서비스 클라이언트(MeshyClient, GeminiClient)는 에디터 메뉴와 같이 쓰려고 Task로 되어 있고,
// 게임 안의 흐름(소환, 몸 굽기 줄)은 코루틴이다. 둘을 잇는 다리가 이것 하나다.
// 끝난 뒤 성공했는지는 부른 쪽이 task.IsFaulted / task.Exception으로 본다.
public sealed class WaitForTask : CustomYieldInstruction
{
    private readonly Task task;

    public WaitForTask(Task task)
    {
        this.task = task;
    }

    public override bool keepWaiting => !task.IsCompleted;
}
