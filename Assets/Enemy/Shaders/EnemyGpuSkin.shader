// 적 무리를 GPU에서 스키닝해 그리는 셰이더.
//
// Animator 없이 애니메이션을 재생하는 방법이다. 클립을 미리 샘플링해 "몇 번 프레임의 몇 번
// 뼈가 어떤 행렬인가"를 텍스처 한 장에 구워 두고(EnemyAnimationBaker), 정점 셰이더가 그것을
// 읽어 정점을 옮긴다. 1000마리가 전부 같은 텍스처를 보므로 마리 수가 늘어도 텍스처 비용은
// 그대로이고, 마리마다 다른 것은 "지금 어느 클립의 몇 퍼센트인가" 네 값뿐이다.
//
// 그 네 값이 _EnemyAnimData이고, 엔티티마다 다른 값을 넣기 위해 DOTS 인스턴싱을 쓴다
// (EnemyAnimationMaterial의 [MaterialProperty] 특성이 이 이름과 연결된다).
//
// 조명은 메인 라이트 + 앰비언트(SH)까지만 한다. URP Lit의 전체 기능(스페큘러, 노멀맵,
// 추가 라이트)은 넣지 않았다 — 화면을 채우는 것은 멀리 있는 잡몹이고, 그 거리에서
// 그 계산은 보이지 않으면서 비용만 마리 수만큼 곱해진다.
Shader "PickMeUp/Enemy GPU Skin"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)

        // 구워 놓은 뼈 행렬. 가로 = 뼈 수 x 3, 세로 = 전체 프레임 수.
        [NoScaleOffset] _BoneTexture ("Bone Matrices", 2D) = "black" {}

        // x = 클립 시작 줄, y = 클립 프레임 수, z = 진행도(0~1), w = 텍스처 세로 크기.
        _EnemyAnimData ("Animation Data", Vector) = (0, 1, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BoneTexture);
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float4 _EnemyAnimData;
        CBUFFER_END

        // 엔티티마다 다른 값을 받는 자리. 이게 없으면 1000마리가 한 몸처럼 같은 동작을 한다.
        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _EnemyAnimData)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

        #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)
        #define _EnemyAnimData UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EnemyAnimData)
        #endif

        // 이번에 읽을 줄(프레임) 번호.
        //
        // 진행도를 프레임 수에 걸쳐 편 뒤 내림한다. 보간은 하지 않는다 — 30fps로 구운 클립을
        // 60fps로 재생해도 잡몹 거리에서는 차이가 보이지 않고, 보간하려면 텍스처를 두 번
        // 읽어야 해서 정점 셰이더 비용이 그대로 두 배가 된다.
        float EnemyAnimRow(float4 animData)
        {
            float frames = max(animData.y, 1.0);
            float local = floor(saturate(animData.z) * (frames - 1.0));
            return animData.x + local;
        }

        // 뼈 하나의 3x4 행렬을 읽는다. 한 줄에 뼈마다 텍셀 셋이 나란히 놓여 있다.
        void ReadBone(int bone, int row, out float4 r0, out float4 r1, out float4 r2)
        {
            int x = bone * 3;
            r0 = LOAD_TEXTURE2D(_BoneTexture, int2(x + 0, row));
            r1 = LOAD_TEXTURE2D(_BoneTexture, int2(x + 1, row));
            r2 = LOAD_TEXTURE2D(_BoneTexture, int2(x + 2, row));
        }

        // 뼈 넷을 가중치로 섞어 정점과 노멀을 옮긴다. Unity의 스키닝과 같은 계산이다.
        void SkinVertex(float3 positionOS, float3 normalOS, float4 indices, float4 weights,
                        out float3 skinnedPosition, out float3 skinnedNormal)
        {
            int row = (int)EnemyAnimRow(_EnemyAnimData);

            skinnedPosition = float3(0, 0, 0);
            skinnedNormal = float3(0, 0, 0);

            float4 position4 = float4(positionOS, 1.0);

            [unroll]
            for (int i = 0; i < 4; i++)
            {
                float weight = weights[i];
                if (weight <= 0.0) continue;

                float4 r0, r1, r2;
                ReadBone((int)indices[i], row, r0, r1, r2);

                skinnedPosition += weight * float3(dot(r0, position4), dot(r1, position4), dot(r2, position4));
                skinnedNormal += weight * float3(dot(r0.xyz, normalOS), dot(r1.xyz, normalOS), dot(r2.xyz, normalOS));
            }
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                // 베이커가 넣어 둔 스킨 정보. 뼈 번호와 가중치를 UV로 실어 보낸다 —
                // 일반 MeshRenderer에는 BLENDINDICES가 오지 않기 때문이다.
                float4 boneIndices : TEXCOORD2;
                float4 boneWeights : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionOS, normalOS;
                SkinVertex(input.positionOS, input.normalOS, input.boneIndices, input.boneWeights,
                           positionOS, normalOS);

                VertexPositionInputs positions = GetVertexPositionInputs(positionOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normalize(TransformObjectToWorldNormal(normalOS));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float3 normal = normalize(input.normalWS);
                half diffuse = saturate(dot(normal, mainLight.direction));

                half3 lighting = mainLight.color * diffuse * mainLight.shadowAttenuation;
                lighting += SampleSH(normal);

                return half4(albedo.rgb * lighting, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 boneIndices : TEXCOORD2;
                float4 boneWeights : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionOS, normalOS;
                SkinVertex(input.positionOS, input.normalOS, input.boneIndices, input.boneWeights,
                           positionOS, normalOS);

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);

                // ApplyShadowBias는 월드 좌표를 밀어 주기만 한다. 클립 좌표로 옮기는 것은 여기서.
                positionWS = ApplyShadowBias(positionWS, normalWS, _LightDirection);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 ShadowFragment(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
