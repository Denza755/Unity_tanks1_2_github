using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace AnyRPG
{
    public class URPMaterialBulkConverter : EditorWindow
    {
        [MenuItem("Tools/Complete Project URP Converter")]
        public static void ShowWindow()
        {
            GetWindow<URPMaterialBulkConverter>("Project Full URP Converter");
        }

        private void OnGUI()
        {
            GUILayout.Label("Универсальный конвертер шейдеров (Creator Kit & Sun Temple)", EditorStyles.boldLabel);
            GUILayout.Space(10);
            EditorGUILayout.HelpBox("Скрипт автоматически находит и переводит ВСЕ материалы (включая The Wasteland LITE, CreatorKit и Sun Temple) на конвейер Unity 6 URP.", MessageType.Warning);
            GUILayout.Space(10);

            if (GUILayout.Button("Запустить полную конвертацию проекта", GUILayout.Height(45)))
            {
                if (EditorUtility.DisplayDialog("Глубокая конвертация", "Это действие принудительно перепишет параметры старых материалов для совместимости с URP. Продолжить?", "Да, конвертировать всё", "Отмена"))
                {
                    ExecuteFullConversion();
                }
            }
        }

        private static void ExecuteFullConversion()
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            Shader urpParticlesUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");

            if (urpLit == null)
            {
                EditorUtility.DisplayDialog("Ошибка", "Базовые шейдеры URP не найдены в проекте! Проверьте настройки Edit -> Project Settings -> Quality.", "OK");
                return;
            }

            string[] searchFolders = new string[] { "Assets" };
            string[] materialGUIDs = AssetDatabase.FindAssets("t:Material", searchFolders);
            int totalPatched = 0;

            foreach (string guid in materialGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat != null)
                {
                    string shaderName = mat.shader != null ? mat.shader.name : "";
                    bool needsConversion = false;
                    bool isLostShader = string.IsNullOrEmpty(shaderName) || shaderName == "Hidden/InternalErrorShader";
                    
                    // Определяем частицы
                    bool isParticleMaterial = shaderName.Contains("Particles") || 
                                              mat.name.ToLower().Contains("smoke") || 
                                              mat.name.ToLower().Contains("fire") || 
                                              mat.name.ToLower().Contains("spark") || 
                                              mat.name.ToLower().Contains("vfx");

                    // Расширенный список для конвертации
                    if (isLostShader || 
                        shaderName.StartsWith("Custom/") || 
                        shaderName.Contains("CreatorKit") || 
                        shaderName.Contains("Sun_Temple") || 
                        shaderName == "Standard" || 
                        shaderName.Contains("Specular") || 
                        shaderName.Contains("Particles") ||
                        shaderName.StartsWith("Legacy Shaders/") ||
                        path.Contains("The Wasteland")) // Добавлена проверка на The Wasteland
                    {
                        needsConversion = true;
                    }

                    if (needsConversion)
                    {
                        Undo.RecordObject(mat, "Deep URP Material Convert");

                        // === СБОР ВСЕХ СВОЙСТВ СТАРОГО ШЕЙДЕРА ===
                        // Albedo / Main
                        Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                        Color mainColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                        
                        // Normal Map
                        Texture bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                        float bumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1.0f;
                        
                        // Emission
                        Texture emissionTex = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
                        Color emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                        
                        // Occlusion
                        Texture occlusionMap = mat.HasProperty("_OcclusionMap") ? mat.GetTexture("_OcclusionMap") : null;
                        float occlusionStrength = mat.HasProperty("_OcclusionStrength") ? mat.GetFloat("_OcclusionStrength") : 1.0f;

                        // Metallic / Specular
                        Texture specGlossMap = mat.HasProperty("_SpecGlossMap") ? mat.GetTexture("_SpecGlossMap") : null;
                        Color specColor = mat.HasProperty("_SpecColor") ? mat.GetColor("_SpecColor") : Color.white;
                        float smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
                        float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0.0f;
                        
                        // Detail Maps
                        Texture detailAlbedo = mat.HasProperty("_DetailAlbedoMap") ? mat.GetTexture("_DetailAlbedoMap") : null;
                        Texture detailNormal = mat.HasProperty("_DetailNormalMap") ? mat.GetTexture("_DetailNormalMap") : null;
                        float detailNormalScale = mat.HasProperty("_DetailNormalMapScale") ? mat.GetFloat("_DetailNormalMapScale") : 1.0f;

                        // Cutoff для листвы/прозрачности
                        float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;

                        // === НАСТРОЙКА ШЕЙДЕРА ===
                        if (isParticleMaterial)
                        {
                            mat.shader = urpParticlesUnlit; 
                            
                            if (mainTex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
                            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mainColor);
                            
                            mat.SetFloat("_Surface", 1.0f); // Transparent
                            mat.SetFloat("_Blend", 0.0f);   // Alpha Blend
                            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                            mat.SetInt("_ZWrite", 0);
                            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                            mat.EnableKeyword("_ALPHABLEND_ON");
                            mat.renderQueue = (int)RenderQueue.Transparent;
                        }
                        else if (shaderName.Contains("Unlit") || shaderName.Contains("UI") || shaderName.Contains("Screen") || shaderName.Contains("Clouds"))
                        {
                            mat.shader = urpUnlit;
                            if (mainTex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
                            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mainColor);
                        }
                        else
                        {
                            // === КОНВЕРТАЦИЯ STANDARD / STANDARD SPECULAR В URP LIT ===
                            mat.shader = urpLit;

                            // 1. Основные карты
                            if (mainTex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
                            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mainColor);
                            
                            // 2. Normal Map
                            if (bumpMap != null && mat.HasProperty("_BumpMap"))
                            {
                                mat.SetTexture("_BumpMap", bumpMap);
                                mat.SetFloat("_BumpScale", bumpScale);
                                mat.EnableKeyword("_NORMALMAP");
                            }

                            // 3. Occlusion Map
                            if (occlusionMap != null && mat.HasProperty("_OcclusionMap"))
                            {
                                mat.SetTexture("_OcclusionMap", occlusionMap);
                                mat.SetFloat("_OcclusionStrength", occlusionStrength);
                                mat.EnableKeyword("_OCCLUSIONMAP");
                            }

                            // 4. Metallic / Specular Workflow
                            // Если есть карта SpecGloss, используем Specular Workflow (1.0)
                            if (specGlossMap != null && mat.HasProperty("_SpecGlossMap"))
                            {
                                mat.SetTexture("_SpecGlossMap", specGlossMap);
                                mat.SetFloat("_WorkflowMode", 1.0f); 
                                mat.EnableKeyword("_SPECULAR_SETUP");
                                if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", specColor);
                            }
                            else
                            {
                                // Иначе используем Metallic Workflow (0.0)
                                mat.SetFloat("_WorkflowMode", 0.0f);
                                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
                            }
                            
                            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

                            // 5. Emission
                            if (emissionTex != null && mat.HasProperty("_EmissionMap"))
                            {
                                mat.SetTexture("_EmissionMap", emissionTex);
                                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
                                mat.EnableKeyword("_EMISSION");
                                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                            }
                            else
                            {
                                mat.DisableKeyword("_EMISSION");
                            }

                            // 6. Detail Maps
                            if (detailAlbedo != null && mat.HasProperty("_DetailAlbedoMap")) mat.SetTexture("_DetailAlbedoMap", detailAlbedo);
                            if (detailNormal != null && mat.HasProperty("_DetailNormalMap")) 
                            {
                                mat.SetTexture("_DetailNormalMap", detailNormal);
                                mat.SetFloat("_DetailNormalMapScale", detailNormalScale);
                            }

                            // 7. Прозрачность и Alpha Clip (для листвы, стекол и т.д.)
                            if (shaderName.Contains("Transparent") || shaderName.Contains("Glass") || shaderName.Contains("WindowGlass") || shaderName.Contains("Fluid") || shaderName.Contains("Slime") || shaderName.Contains("Clouds"))
                            {
                                mat.SetFloat("_Surface", 1.0f); 
                                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                                mat.SetInt("_ZWrite", 0);
                                mat.DisableKeyword("_ALPHATEST_ON");
                                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                                mat.renderQueue = (int)RenderQueue.Transparent;
                            }
                            else if (shaderName.Contains("Foliage") || shaderName.Contains("Leave") || shaderName.Contains("Vegetation") || mat.name.ToLower().Contains("foliage") || mat.name.ToLower().Contains("leave"))
                            {
                                mat.SetFloat("_Surface", 0.0f); 
                                mat.SetFloat("_AlphaClip", 1.0f); 
                                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff); 
                                mat.EnableKeyword("_ALPHATEST_ON");
                                mat.renderQueue = (int)RenderQueue.AlphaTest;
                            }
                        }

                        EditorUtility.SetDirty(mat);
                        totalPatched++;
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Project URP Converter] Конвертация завершена! Успешно обновлено материалов: {totalPatched}.");
            EditorUtility.DisplayDialog("Успех", $"Глубокая конвертация завершена!\nУспешно адаптировано материалов под URP: {totalPatched}.", "OK");
        }
    }
}