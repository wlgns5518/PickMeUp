using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 탭(세그먼트) 한 줄. 어두운 트랙 위에 칸이 나란히 서고, 고른 칸만 밝은 판·흰 글자·선택색 밑줄을 갖는다.
//
// 장비 제작소의 [장비 제작 | 장비 합성], 소환 배너, 장비 분류, 제작 방식·난이도가 모두 이 한 모양이다.
// 탭은 "같은 자리에서 보는 내용을 바꾸는 것"에만 쓴다 — 실행은 버튼이다.
[DisallowMultipleComponent]
public class UiTabs : MonoBehaviour
{
    private class Tab
    {
        public Button Button;
        public Image Fill;
        public Image Underline;
        public TMP_Text Label;
    }

    private readonly List<Tab> tabs = new List<Tab>();
    private int selected = -1;

    public event Action<int> Changed;

    public int Selected => selected;
    public RectTransform Rect => (RectTransform)transform;

    /// 만들기. 자리와 폭은 돌려받은 Rect에 잡는다. 칸은 폭을 똑같이 나눠 갖는다.
    public static UiTabs Create(RectTransform parent, string name, IReadOnlyList<string> labels, float fontSize = UiTheme.FontBody)
    {
        RectTransform rect = UiKit.Node(parent, name);
        rect.sizeDelta = new Vector2(480f, UiTheme.TabHeight);
        UiKit.Rounded(rect, "Track", UiTheme.SurfaceSunken, UiTheme.RadiusM);

        var tabsView = rect.gameObject.AddComponent<UiTabs>();
        int count = labels.Count;
        const float inset = 4f;

        for (int i = 0; i < count; i++)
        {
            int index = i;

            RectTransform cell = UiKit.Node(rect, "Tab_" + i);
            cell.anchorMin = new Vector2((float)i / count, 0f);
            cell.anchorMax = new Vector2((float)(i + 1) / count, 1f);
            cell.pivot = new Vector2(0.5f, 0.5f);
            cell.offsetMin = new Vector2(inset, inset);
            cell.offsetMax = new Vector2(-inset, -inset);

            var tab = new Tab
            {
                Fill = UiKit.Rounded(cell, "Fill", Color.clear, UiTheme.RadiusS),
                Label = UiKit.Text(cell, "Label", labels[i], fontSize, UiTheme.TextSecondary, TextAlignmentOptions.Center),
            };
            tab.Fill.raycastTarget = true;

            tab.Underline = UiKit.Rounded(cell, "Underline", UiTheme.Selection, 2);
            UiKit.BottomStretch(tab.Underline.rectTransform, 0f, UiTheme.SelectionWidth, 18f, 18f);

            tab.Button = cell.gameObject.AddComponent<Button>();
            tab.Button.targetGraphic = tab.Fill;
            tab.Button.transition = Selectable.Transition.None;
            tab.Button.onClick.AddListener(() => tabsView.Select(index, true));

            tabsView.tabs.Add(tab);
        }

        tabsView.Select(0, false);
        return tabsView;
    }

    public void Select(int index, bool notify)
    {
        if (index < 0 || index >= tabs.Count) return;

        bool changed = index != selected;
        selected = index;

        for (int i = 0; i < tabs.Count; i++)
        {
            bool on = i == selected;
            tabs[i].Fill.color = on ? UiTheme.SurfaceHover : Color.clear;
            tabs[i].Underline.enabled = on;
            tabs[i].Label.color = on ? UiTheme.TextPrimary : UiTheme.TextSecondary;
        }

        if (notify && changed) Changed?.Invoke(selected);
    }

    // 칸을 다시 만들지 않고 글자만 바꾼다("1파티 · 3명"처럼 숫자가 붙는 탭).
    public void SetLabel(int index, string text)
    {
        if (index >= 0 && index < tabs.Count) tabs[index].Label.text = text;
    }
}
