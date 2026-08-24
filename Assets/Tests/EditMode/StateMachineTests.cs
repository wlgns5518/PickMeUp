using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// 상태머신 자체의 규칙. 유닛이나 씬 없이 가짜 컨텍스트로 돌린다.
//
// 여기서 고정하는 것은 두 가지다:
//  - 전이는 지연 적용된다(Enter 안에서 다시 전이해도 재귀가 아니라 루프로 풀린다)
//  - 전역 전이는 상태의 Update보다 먼저, 한 프레임에 하나만, 등록 순서대로 걸린다
// 둘 다 어긋나도 컴파일은 되고 증상은 "가끔 이상하다"로만 나타나는 종류라 테스트로 묶어 둔다.
public class StateMachineTests
{
    private class Ctx
    {
    }

    private class Recording : IState<Ctx>
    {
        private readonly List<string> log;
        private readonly string name;

        public Action OnEnter;
        public Action OnUpdate;

        public Recording(string name, List<string> log)
        {
            this.name = name;
            this.log = log;
        }

        public void Enter()
        {
            log.Add(name + ".Enter");
            OnEnter?.Invoke();
        }

        public void Update()
        {
            log.Add(name + ".Update");
            OnUpdate?.Invoke();
        }

        public void Exit()
        {
            log.Add(name + ".Exit");
        }
    }

    private class Global : GlobalTransition<Ctx>
    {
        private readonly Func<IState<Ctx>, IState<Ctx>> evaluate;

        public Global(Func<IState<Ctx>, IState<Ctx>> evaluate)
        {
            this.evaluate = evaluate;
        }

        public override IState<Ctx> Evaluate(Ctx context, IState<Ctx> currentState) => evaluate(currentState);
    }

    private List<string> log;
    private Ctx ctx;
    private StateMachine<Ctx> machine;

    [SetUp]
    public void SetUp()
    {
        log = new List<string>();
        ctx = new Ctx();
        machine = new StateMachine<Ctx>();
    }

    [Test]
    public void 진입하면_Enter가_불린다()
    {
        var a = new Recording("A", log);
        machine.Initialize(ctx, a);

        Assert.AreEqual(new[] { "A.Enter" }, log.ToArray());
        Assert.AreSame(a, machine.CurrentState);
    }

    [Test]
    public void 전이는_Update가_끝난_뒤에_적용된다()
    {
        var a = new Recording("A", log);
        var b = new Recording("B", log);
        a.OnUpdate = () => machine.ChangeState(b);

        machine.Initialize(ctx, a);
        machine.Update();

        // A.Update가 끝까지 돈 뒤에 전이가 일어나고, B.Update는 이번 프레임에 돌지 않는다.
        Assert.AreEqual(new[] { "A.Enter", "A.Update", "A.Exit", "B.Enter" }, log.ToArray());
    }

    [Test]
    public void Enter가_다시_전이해도_재귀가_아니라_이어서_풀린다()
    {
        var a = new Recording("A", log);
        var b = new Recording("B", log);
        var c = new Recording("C", log);
        a.OnEnter = () => machine.ChangeState(b);
        b.OnEnter = () => machine.ChangeState(c);

        machine.Initialize(ctx, a);

        Assert.AreEqual(new[] { "A.Enter", "A.Exit", "B.Enter", "B.Exit", "C.Enter" }, log.ToArray());
        Assert.AreSame(c, machine.CurrentState);
    }

    [Test]
    public void 서로를_부르는_상태는_스택을_무너뜨리지_않고_로그로_잡힌다()
    {
        var a = new Recording("A", log);
        var b = new Recording("B", log);
        a.OnEnter = () => machine.ChangeState(b);
        b.OnEnter = () => machine.ChangeState(a);

        LogAssert.Expect(LogType.Error, new Regex("StateMachine.*전이"));

        machine.Initialize(ctx, a); // 예전 구조라면 여기서 스택오버플로우가 났다.

        Assert.IsNotNull(machine.CurrentState);
    }

    [Test]
    public void 같은_상태로의_전이는_아무_일도_하지_않는다()
    {
        var a = new Recording("A", log);
        machine.Initialize(ctx, a);
        log.Clear();

        machine.ChangeState(a);
        machine.Update();

        Assert.AreEqual(new[] { "A.Update" }, log.ToArray(), "Exit/Enter가 다시 불려서는 안 된다");
    }

    [Test]
    public void 전역_전이는_상태의_Update보다_먼저_걸린다()
    {
        var a = new Recording("A", log);
        var dead = new Recording("Dead", log);
        bool fire = true;

        machine.Initialize(ctx, a, new GlobalTransition<Ctx>[] { new Global(_ => fire ? dead : null) });
        log.Clear();

        machine.Update();

        // A.Update는 아예 돌지 않아야 한다 — 이미 끊겼어야 할 상태이기 때문.
        Assert.AreEqual(new[] { "A.Exit", "Dead.Enter", "Dead.Update" }, log.ToArray());
        Assert.IsTrue(fire);
    }

    [Test]
    public void 전역_전이는_이미_그_상태면_다시_걸리지_않는다()
    {
        var a = new Recording("A", log);
        var dead = new Recording("Dead", log);

        machine.Initialize(ctx, a, new GlobalTransition<Ctx>[] { new Global(_ => dead) });
        machine.Update();
        log.Clear();

        machine.Update();

        Assert.AreEqual(new[] { "Dead.Update" }, log.ToArray(), "Exit/Enter가 매 프레임 다시 불려서는 안 된다");
    }

    [Test]
    public void 전역_전이는_한_프레임에_하나만_걸리고_등록_순서가_우선순위다()
    {
        var a = new Recording("A", log);
        var first = new Recording("First", log);
        var second = new Recording("Second", log);

        machine.Initialize(ctx, a, new GlobalTransition<Ctx>[]
        {
            new Global(_ => first),
            new Global(_ => second),
        });
        log.Clear();

        machine.Update();

        Assert.AreSame(first, machine.CurrentState, "앞에 등록된 것이 이긴다");
        CollectionAssert.DoesNotContain(log, "Second.Enter");
    }

    [Test]
    public void 전역_전이가_없으면_상태가_정상적으로_돈다()
    {
        var a = new Recording("A", log);

        machine.Initialize(ctx, a, new GlobalTransition<Ctx>[] { new Global(_ => null) });
        log.Clear();

        machine.Update();

        Assert.AreEqual(new[] { "A.Update" }, log.ToArray());
    }
}
