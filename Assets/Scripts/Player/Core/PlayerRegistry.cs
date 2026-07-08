using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 존재하는 플레이어 AI를 관리하는 싱글톤 레지스트리.
/// PlayerAIController가 Awake에서 Register, 사망 시 Unregister.
/// </summary>
public class PlayerRegistry : MonoBehaviour
{
    public static PlayerRegistry Instance { get; private set; }

    private readonly List<PlayerAIController> _players = new();

    public IReadOnlyList<PlayerAIController> Players => _players;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Register(PlayerAIController player)
    {
        if (!_players.Contains(player))
            _players.Add(player);
    }

    public void Unregister(PlayerAIController player)
    {
        _players.Remove(player);
    }
}
