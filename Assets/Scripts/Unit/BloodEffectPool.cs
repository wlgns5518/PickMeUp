using System.Collections.Generic;
using UnityEngine;

public class BloodEffectPool : MonoBehaviour
{
    [SerializeField] private int poolSizePerPrefab = 20;

    private static BloodEffectPool instance;

    private readonly Dictionary<GameObject, List<GameObject>> pools = new Dictionary<GameObject, List<GameObject>>();

    public static BloodEffectPool Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject holder = new GameObject(nameof(BloodEffectPool));
                instance = holder.AddComponent<BloodEffectPool>();
            }

            return instance;
        }
    }

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        List<GameObject> pool = GetOrCreatePool(prefab);
        GameObject instanceObject = FindInactiveInstance(pool);
        if (instanceObject == null)
        {
            instanceObject = Instantiate(prefab, transform);
            instanceObject.SetActive(false);
            pool.Add(instanceObject);
        }

        instanceObject.transform.SetPositionAndRotation(position, rotation);
        instanceObject.SetActive(true);

        return instanceObject;
    }

    private static GameObject FindInactiveInstance(List<GameObject> pool)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (!pool[i].activeSelf) return pool[i];
        }

        return null;
    }

    private List<GameObject> GetOrCreatePool(GameObject prefab)
    {
        if (pools.TryGetValue(prefab, out List<GameObject> existingPool)) return existingPool;

        List<GameObject> pool = new List<GameObject>(poolSizePerPrefab);
        for (int i = 0; i < poolSizePerPrefab; i++)
        {
            GameObject pooledInstance = Instantiate(prefab, transform);
            pooledInstance.SetActive(false);
            pool.Add(pooledInstance);
        }

        pools[prefab] = pool;
        return pool;
    }
}
