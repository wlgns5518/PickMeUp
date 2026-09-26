// 젬을 사는 곳. 재화 칩의 + 버튼(UiCurrencyChip)이 이리로 온다.
//
// 젬은 나중에 유료 결제로 판다(2026-09-26 사용자 결정). 지금은 버튼 자리만 먼저 두고 누르면 준비 중이라고 알린다 —
// 결제를 붙일 때는 이 함수 하나만 바꾸면 상단바와 시설 화면 머리줄의 + 버튼이 모두 따라온다.
public static class GemShop
{
    private const string NotReady = "젬 구매는 아직 준비 중입니다.";

    public static void Open(UiToast toast)
    {
        if (toast != null) toast.Show(NotReady);
    }
}
