
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BFX
{
//[ExecuteAlways]
    public class BFX_DecalSettings : IScriptInstance
    {
        public         BFX_BloodSettings BloodSettings;
        public         Transform         parent;
        public         float             TimeHeightMax = 3.1f;
        public         float             TimeHeightMin = -0.1f;
        [Space] public Vector3           TimeScaleMax  = Vector3.one;
        public         Vector3           TimeScaleMin  = Vector3.one;
        [Space] public Vector3           TimeOffsetMax = Vector3.zero;
        public         Vector3           TimeOffsetMin = Vector3.zero;
        [Space] public AnimationCurve    TimeByHeight  = AnimationCurve.Linear(0, 0, 1, 1);

        private Vector3 startOffset;
        private Vector3 startScale;
        private float   timeDelay;

        Transform           t;
        BFX_ShaderProperies shaderProperies;
        private Renderer    _decal;

        Vector3         averageRay;
        bool            isPositionInitialized;
        private Vector3 initializedPosition;
        private bool    isInitialized;

        private void Initialize()
        {
            startOffset                         =  transform.localPosition;
            startScale                          =  transform.localScale;
            t                                   =  transform;
            shaderProperies                     =  GetComponent<BFX_ShaderProperies>();
            shaderProperies.OnAnimationFinished += ShaderCurve_OnAnimationFinished;
            isInitialized                       =  true;
            _decal                              =  GetComponent<Renderer>();
            _decal.enabled                      =  false;
        }

        private void ShaderCurve_OnAnimationFinished()
        {
            _decal.enabled = false;
        }

        internal override void ManualUpdate()
        {
            if (!isInitialized) Initialize();
            if (!isPositionInitialized) InitializePosition();
            if (shaderProperies.CanUpdate && initializedPosition.x < float.PositiveInfinity) transform.position = initializedPosition;
        }

        void InitializePosition()
        {
            var currentHeight = parent.position.y;

            float ground = currentHeight;
            if (BloodSettings.AutomaticGroundHeightDetection)
            {
                var   raycasts = Physics.RaycastAll((parent.position + parent.right), Vector3.down, 5);
                float max      = float.MinValue;
                foreach (var raycast in raycasts)
                {
                    if (raycast.point.y > max)
                    {
                        max = raycast.point.y;
                    }
                }

                if (raycasts.Length == 0) max        = ground;
                if (max             < ground) ground = max;
            }
            else
            {
                ground = BloodSettings.GroundHeight;
            }

            var currentScale        = parent.localScale.y;
            var scaledTimeHeightMax = TimeHeightMax * currentScale;
            var scaledTimeHeightMin = TimeHeightMin * currentScale;

            if (currentHeight - ground >= scaledTimeHeightMax || currentHeight - ground <= scaledTimeHeightMin)
            {
                _decal.enabled = false;
            }
            else
            {
                _decal.enabled = true;
            }

            float diff = (parent.position.y - ground) / scaledTimeHeightMax;
            diff = Mathf.Abs(diff);

            var scaleMul = Vector3.Lerp(TimeScaleMin, TimeScaleMax, diff);
            t.localScale = new Vector3(scaleMul.x * startScale.x, startScale.y, scaleMul.z * startScale.z);

            var lastOffset = Vector3.Lerp(TimeOffsetMin, TimeOffsetMax, diff);
            t.localPosition = startOffset + lastOffset;
            t.position      = new Vector3(t.position.x, ground + 0.01f, t.position.z);


            timeDelay = TimeByHeight.Evaluate(diff);

            shaderProperies.CanUpdate = false;
            Invoke(nameof(EnableDecalAnimation), Mathf.Max(0, timeDelay / BloodSettings.AnimationSpeed));

           
            isPositionInitialized = true;
        }
        
        internal override void OnEnableExtended()
        {
          
        }

        internal override void OnDisableExtended()
        {
            isPositionInitialized = false;
            initializedPosition   = Vector3.positiveInfinity;
        }

        Vector3 startTest, dirTest;

        void EnableDecalAnimation()
        {
            shaderProperies.CanUpdate = true;
            initializedPosition     = transform.position;
        }

        private void OnDrawGizmos()
        {
            if (t == null) t = transform;
            Gizmos.color  = new Color(49 / 255.0f, 136 / 255.0f, 1, 0.03f);
            Gizmos.matrix = Matrix4x4.TRS(t.position, t.rotation, t.lossyScale);
            Gizmos.DrawCube(Vector3.zero, Vector3.one);

            Gizmos.color = new Color(49 / 255.0f, 136 / 255.0f, 1, 0.85f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

            Debug.DrawRay(startTest, -dirTest);
        }
    }
}