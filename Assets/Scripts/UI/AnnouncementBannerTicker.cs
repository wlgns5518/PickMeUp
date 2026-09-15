using UnityEngine;

// AnnouncementBanner의 시간을 굴리는 부품. 배너 루트에 붙는다.
//
// 예전에는 배너를 가진 창(시설 창 다섯, 전투 HUD)이 각자 Update에서 매 프레임 Tick을 불렀다.
// 배너는 거의 언제나 꺼져 있는데도 창 수만큼 Update가 헛돌았다. 배너 루트는 떠 있는 동안에만
// 켜져 있으므로(AnnouncementBanner.BeginNext / 페이드아웃 끝), 루트에 붙이면 보일 때만 돈다.
[DisallowMultipleComponent]
public class AnnouncementBannerTicker : MonoBehaviour
{
    private AnnouncementBanner banner;

    public void Bind(AnnouncementBanner owner) => banner = owner;

    private void Update() => banner?.Tick(Time.deltaTime);
}
