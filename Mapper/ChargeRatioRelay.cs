/// <summary>
/// Scan()에서 계산한 충전 비율을, 바로 다음에 동기적으로 호출되는
/// MapperDataCloudManager.WantSpawnMapperDataCloud()로 전달하기 위한 아주 짧은 중계용 저장소.
/// (같은 호출 스택 안에서 곧바로 소비되므로 네트워크나 타이밍 문제와 무관함)
/// </summary>
public static class ChargeRatioRelay
{
    private static float s_pendingRatio = -1f;
    private static int s_pendingPlayerID = -1;

    public static void Stash(int playerID, float ratio)
    {
        s_pendingPlayerID = playerID;
        s_pendingRatio = ratio;
    }

    public static bool TryConsume(int expectedPlayerID, out float ratio)
    {
        ratio = 0f;
        if (s_pendingRatio < 0f) return false;
        if (s_pendingPlayerID != expectedPlayerID) return false; // 내 값이 아니면 절대 안 건드림
        ratio = s_pendingRatio;
        s_pendingRatio = -1f;
        s_pendingPlayerID = -1;
        return true;
    }
}
