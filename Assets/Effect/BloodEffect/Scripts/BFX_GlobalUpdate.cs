using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;


namespace BFX
{
    public partial class GlobalUpdate : MonoBehaviour
    {
        public static GlobalUpdate          Instance;
        public static HashSet<IScriptInstance> ScriptInstances = new HashSet<IScriptInstance>();



        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RunOnStart()
        {
            // (PickMeUp 수정) 원래는 Destroy(Instance)로 컴포넌트만 지웠다. 게임오브젝트가 HideAndDontSave라
            // 에디터에서는 플레이를 멈춰도 지워지지 않고, 도메인 리로드로 Instance는 이미 비어 있어 아무것도
            // 지우지 못했다 — 플레이할 때마다 BFX_GlobalUpdate가 하나씩 쌓였다(편집 모드에서 ExecuteAlways로
            // 만든 것도 같다). 플레이를 시작할 때 남아 있는 것을 전부 치운다. 빌드에서는 이 시점에 아무것도 없다.
            foreach (var stale in Resources.FindObjectsOfTypeAll<GlobalUpdate>())
            {
                if (stale != null) Destroy(stale.gameObject);
            }
            Instance = null;

            ScriptInstances.Clear();
        }

        public static void CreateInstanceIfRequired()
        {
            if (Instance != null) return;

var existing = Object.FindObjectsByType<GlobalUpdate>(FindObjectsInactive.Exclude);
            if (existing.Length > 0)
            {
                Instance = existing[0];

                for (int i = 1; i < existing.Length; i++)
                    Object.Destroy(existing[i].gameObject);
                return;
            }

            var go = new GameObject("BFX_GlobalUpdate")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Instance = go.AddComponent<GlobalUpdate>();
        }
        
        void Update()
        {
            if (Instance != this) return;

            foreach (var iScriptInstance in ScriptInstances)
            {
                if (iScriptInstance.CanUpdate) iScriptInstance.ManualUpdate();
            }
           
        }

    
        void OnEnable()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Camera.onPreCull    += OnBeforeCameraRendering;
            }
        }

        void OnDisable()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Camera.onPreCull    -= OnBeforeCameraRendering;
            }
           
        }

        private void OnBeforeCameraRendering(Camera cam)
        {
            if (cam.renderingPath == RenderingPath.Forward) cam.depthTextureMode |= DepthTextureMode.Depth;
        }
        
    }
}