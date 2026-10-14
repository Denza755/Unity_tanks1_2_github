using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Tanks.Complete
{
    /// <summary>
    /// Open World Biome Sandbox GameManager для Unity 6.
    /// Полностью переписан под режим единого открытого мира:
    /// 1. Авто-старт минуя MainMenuCanvas / Menus.
    /// 2. Игрок 0 = Синий ATV (человек), Индексы 1..3 = Боты биомов.
    /// 3. Камера зафиксирована строго на трансформе игрока 0 (без отдаления).
    /// 4. Бесконечная корутина SandboxLoop с локальным возрождением через 3 секунды.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        /// <summary>
        /// Вложенный тип PlayerData, ожидаемый GameUIHandler.cs и остальными системами Tanks.
        /// </summary>
        [System.Serializable]
        public struct PlayerData
        {
            public Color m_PlayerColor;
            public Transform m_SpawnPoint;
            public int m_PlayerNumber;
            public GameObject m_TankPrefab;
            public bool m_IsComputer;
            public int m_ControlIndex;

            // Свойства-алиасы для максимальной совместимости
            public GameObject tankPrefab { get => m_TankPrefab; set => m_TankPrefab = value; }
            public bool IsComputer { get => m_IsComputer; set => m_IsComputer = value; }
            public bool isComputer { get => m_IsComputer; set => m_IsComputer = value; }
            public int ControlIndex { get => m_ControlIndex; set => m_ControlIndex = value; }
            public int controlIndex { get => m_ControlIndex; set => m_ControlIndex = value; }
            public Color playerColor { get => m_PlayerColor; set => m_PlayerColor = value; }
            public int playerNumber { get => m_PlayerNumber; set => m_PlayerNumber = value; }
            public Transform spawnPoint { get => m_SpawnPoint; set => m_SpawnPoint = value; }
        }

        [Header("Camera & UI")]
        public CameraControl m_CameraControl;
        public Text m_MessageText;
        public GameObject m_MainMenuCanvas;
        public GameObject m_Menus;

        [Header("Tank Prefabs (Player & Biomes)")]
        [Tooltip("Индекс 0: Танк игрока (Синий ATV)")]
        public GameObject m_Tank1Prefab;
        [Tooltip("Индекс 1: Бот Биома 1")]
        public GameObject m_Tank2Prefab;
        [Tooltip("Индекс 2: Бот Биома 2")]
        public GameObject m_Tank3Prefab;
        [Tooltip("Индекс 3: Бот Биома 3")]
        public GameObject m_Tank4Prefab;

        [Tooltip("Резервный префаб на случай отсутствия индивидуальных префабов")]
        public GameObject m_TankPrefab;

        [Header("Spawn Points / Tank Managers")]
        public TankManager[] m_SpawnPoints;

        // Свойства обратной совместимости для GameUIHandler и других скриптов проекта
        public TankManager[] m_Tanks { get => m_SpawnPoints; set => m_SpawnPoints = value; }
        public int m_NumRoundsToWin = 5;
        public float m_StartDelay = 3f;
        public float m_EndDelay = 3f;
        public int m_RoundNumber = 1;

        [Header("Active Player Setup")]
        public PlayerData[] m_PlayerData;

        // Защита от запуска параллельных корутин возрождения одного и того же танка
        private readonly HashSet<int> m_RespawningTanks = new HashSet<int>();
        private Coroutine m_SandboxCoroutine;

        private void Start()
        {
            // 1. Авто-старт: автоматически находим и отключаем MainMenuCanvas или Menus
            DisableMenuCanvases();

            // 2. Формируем массив PlayerData[] из 4 элементов с фиксированными ролями
            PlayerData[] players = CreateFixedPlayersData();

            // 3. Мгновенно запускаем игру минуя меню выбора
            StartGame(players);
        }

        /// <summary>
        /// Правило 1: Автоматически находит и отключает меню.
        /// </summary>
        private void DisableMenuCanvases()
        {
            if (m_MainMenuCanvas != null)
            {
                m_MainMenuCanvas.SetActive(false);
            }

            if (m_Menus != null)
            {
                m_Menus.SetActive(false);
            }

            GameObject foundMainMenu = GameObject.Find("MainMenuCanvas");
            if (foundMainMenu != null)
            {
                foundMainMenu.SetActive(false);
            }

            GameObject foundMenus = GameObject.Find("Menus");
            if (foundMenus != null)
            {
                foundMenus.SetActive(false);
            }

            #if UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            #else
            Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
            #endif

            foreach (Canvas c in canvases)
            {
                if (c != null && c.gameObject != null)
                {
                    string lower = c.gameObject.name.ToLower();
                    if (lower.Contains("mainmenu") || lower.Equals("menus") || lower.Contains("menu canvas"))
                    {
                        c.gameObject.SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// Правило 2: Фиксация ролей:
        /// Индекс 0: Игрок, m_Tank1Prefab, IsComputer = false, ControlIndex = 1
        /// Индексы 1..3: Боты с m_Tank2Prefab, m_Tank3Prefab, m_Tank4Prefab, IsComputer = true, ControlIndex = -1
        /// </summary>
        public PlayerData[] CreateFixedPlayersData()
        {
            PlayerData[] players = new PlayerData[4];

            // Индекс 0: Игрок (Синий ATV)
            players[0] = new PlayerData
            {
                m_TankPrefab = m_Tank1Prefab != null ? m_Tank1Prefab : m_TankPrefab,
                IsComputer = false,
                ControlIndex = 1,
                m_PlayerNumber = 1,
                m_PlayerColor = new Color(0.18f, 0.44f, 1.0f),
                m_SpawnPoint = (m_SpawnPoints != null && m_SpawnPoints.Length > 0 && m_SpawnPoints[0] != null) ? m_SpawnPoints[0].m_SpawnPoint : null
            };

            // Индекс 1: Бот 1
            players[1] = new PlayerData
            {
                m_TankPrefab = m_Tank2Prefab != null ? m_Tank2Prefab : m_TankPrefab,
                IsComputer = true,
                ControlIndex = -1,
                m_PlayerNumber = 2,
                m_PlayerColor = new Color(0.9f, 0.15f, 0.15f),
                m_SpawnPoint = (m_SpawnPoints != null && m_SpawnPoints.Length > 1 && m_SpawnPoints[1] != null) ? m_SpawnPoints[1].m_SpawnPoint : null
            };

            // Индекс 2: Бот 2
            players[2] = new PlayerData
            {
                m_TankPrefab = m_Tank3Prefab != null ? m_Tank3Prefab : m_TankPrefab,
                IsComputer = true,
                ControlIndex = -1,
                m_PlayerNumber = 3,
                m_PlayerColor = new Color(0.15f, 0.85f, 0.25f),
                m_SpawnPoint = (m_SpawnPoints != null && m_SpawnPoints.Length > 2 && m_SpawnPoints[2] != null) ? m_SpawnPoints[2].m_SpawnPoint : null
            };

            // Индекс 3: Бот 3
            players[3] = new PlayerData
            {
                m_TankPrefab = m_Tank4Prefab != null ? m_Tank4Prefab : m_TankPrefab,
                IsComputer = true,
                ControlIndex = -1,
                m_PlayerNumber = 4,
                m_PlayerColor = new Color(0.95f, 0.75f, 0.1f),
                m_SpawnPoint = (m_SpawnPoints != null && m_SpawnPoints.Length > 3 && m_SpawnPoints[3] != null) ? m_SpawnPoints[3].m_SpawnPoint : null
            };

            return players;
        }

        public void StartGame(PlayerData[] players)
        {
            m_PlayerData = players;
            StartGame();
        }

        public void StartGame()
        {
            if (m_PlayerData == null || m_PlayerData.Length < 4)
            {
                m_PlayerData = CreateFixedPlayersData();
            }

            SpawnAllTanks();
            SetCameraTargets();

            if (m_SandboxCoroutine != null)
            {
                StopCoroutine(m_SandboxCoroutine);
            }
            m_SandboxCoroutine = StartCoroutine(SandboxLoop());
        }

        /// <summary>
        /// Спавнит все 4 танка и вызывает TankManager.Setup(this).
        /// </summary>
        private void SpawnAllTanks()
        {
            if (m_SpawnPoints == null) return;

            for (int i = 0; i < m_SpawnPoints.Length; i++)
            {
                if (m_SpawnPoints[i] == null) continue;

                GameObject prefabToSpawn = GetTankPrefab(i);

                if (prefabToSpawn != null && m_SpawnPoints[i].m_SpawnPoint != null)
                {
                    if (m_SpawnPoints[i].m_Instance != null)
                    {
                        Destroy(m_SpawnPoints[i].m_Instance);
                    }

                    m_SpawnPoints[i].m_Instance = Instantiate(
                        prefabToSpawn,
                        m_SpawnPoints[i].m_SpawnPoint.position,
                        m_SpawnPoints[i].m_SpawnPoint.rotation
                    );

                    m_SpawnPoints[i].m_PlayerNumber = i + 1;

                    if (m_PlayerData != null && i < m_PlayerData.Length)
                    {
                        SyncTankManagerData(m_SpawnPoints[i], m_PlayerData[i]);
                    }

                    // Исправление CS7036: передаем 'this' (GameManager) в Setup
                    m_SpawnPoints[i].Setup(this);
                }
            }
        }

        private GameObject GetTankPrefab(int index)
        {
            if (m_PlayerData != null && index < m_PlayerData.Length && m_PlayerData[index].m_TankPrefab != null)
            {
                return m_PlayerData[index].m_TankPrefab;
            }

            switch (index)
            {
                case 0: return m_Tank1Prefab != null ? m_Tank1Prefab : m_TankPrefab;
                case 1: return m_Tank2Prefab != null ? m_Tank2Prefab : m_TankPrefab;
                case 2: return m_Tank3Prefab != null ? m_Tank3Prefab : m_TankPrefab;
                case 3: return m_Tank4Prefab != null ? m_Tank4Prefab : m_TankPrefab;
                default: return m_TankPrefab;
            }
        }

        private void SyncTankManagerData(TankManager tankManager, PlayerData data)
        {
            if (tankManager == null) return;

            tankManager.m_PlayerColor = data.m_PlayerColor;
            tankManager.m_PlayerNumber = data.m_PlayerNumber;

            var type = typeof(TankManager);

            // IsComputer
            var isCompField = type.GetField("m_IsComputer") ?? type.GetField("IsComputer") ?? type.GetField("isComputer");
            if (isCompField != null) isCompField.SetValue(tankManager, data.IsComputer);
            var isCompProp = type.GetProperty("IsComputer") ?? type.GetProperty("isComputer");
            if (isCompProp != null && isCompProp.CanWrite) isCompProp.SetValue(tankManager, data.IsComputer);

            // ControlIndex
            var ctrlField = type.GetField("m_ControlIndex") ?? type.GetField("ControlIndex") ?? type.GetField("controlIndex");
            if (ctrlField != null) ctrlField.SetValue(tankManager, data.ControlIndex);
            var ctrlProp = type.GetProperty("ControlIndex") ?? type.GetProperty("controlIndex");
            if (ctrlProp != null && ctrlProp.CanWrite) ctrlProp.SetValue(tankManager, data.ControlIndex);
        }

        /// <summary>
        /// Правило 3: Камера держит в качестве цели ТОЛЬКО танк игрока (индекс 0).
        /// </summary>
        private void SetCameraTargets()
        {
            if (m_CameraControl == null) return;

            if (m_SpawnPoints != null && m_SpawnPoints.Length > 0 && m_SpawnPoints[0] != null && m_SpawnPoints[0].m_Instance != null)
            {
                m_CameraControl.m_Targets = new Transform[] { m_SpawnPoints[0].m_Instance.transform };
            }
            else
            {
                m_CameraControl.m_Targets = new Transform[0];
            }
        }

        /// <summary>
        /// Правило 4: Бесшовный бесконечный цикл открытого мира (SandboxLoop).
        /// </summary>
        private IEnumerator SandboxLoop()
        {
            ResetAllTanks();
            EnableTankControl();

            if (m_MessageText != null)
            {
                m_MessageText.text = string.Empty;
            }

            WaitForSeconds checkInterval = new WaitForSeconds(1.0f);

            while (true)
            {
                yield return checkInterval;

                if (m_SpawnPoints == null) continue;

                for (int i = 0; i < m_SpawnPoints.Length; i++)
                {
                    if (m_SpawnPoints[i] != null && m_SpawnPoints[i].m_Instance != null)
                    {
                        // Если танк уничтожен (!activeSelf) и еще не возрождается
                        if (!m_SpawnPoints[i].m_Instance.activeSelf && !m_RespawningTanks.Contains(i))
                        {
                            StartCoroutine(RespawnTank(i));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Правило 4: Задержка 3 секунды, затем Reset() и EnableControl() в точке биома.
        /// </summary>
        private IEnumerator RespawnTank(int tankIndex)
        {
            m_RespawningTanks.Add(tankIndex);

            yield return new WaitForSeconds(3.0f);

            if (m_SpawnPoints != null && tankIndex < m_SpawnPoints.Length && m_SpawnPoints[tankIndex] != null)
            {
                m_SpawnPoints[tankIndex].Reset();
                m_SpawnPoints[tankIndex].EnableControl();

                if (tankIndex == 0)
                {
                    SetCameraTargets();
                }
            }

            m_RespawningTanks.Remove(tankIndex);
        }

        private void ResetAllTanks()
        {
            if (m_SpawnPoints == null) return;

            for (int i = 0; i < m_SpawnPoints.Length; i++)
            {
                if (m_SpawnPoints[i] != null)
                {
                    m_SpawnPoints[i].Reset();
                }
            }
        }

        private void EnableTankControl()
        {
            if (m_SpawnPoints == null) return;

            for (int i = 0; i < m_SpawnPoints.Length; i++)
            {
                if (m_SpawnPoints[i] != null)
                {
                    m_SpawnPoints[i].EnableControl();
                }
            }
        }

        private void DisableTankControl()
        {
            if (m_SpawnPoints == null) return;

            for (int i = 0; i < m_SpawnPoints.Length; i++)
            {
                if (m_SpawnPoints[i] != null)
                {
                    m_SpawnPoints[i].DisableControl();
                }
            }
        }
    }
}