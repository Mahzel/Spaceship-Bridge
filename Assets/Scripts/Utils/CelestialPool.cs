using System.Collections.Generic;
using UnityEngine;

public class ObjectPool<T> where T : Component
{
    private Stack<T> pool;
    private T prefab;
    private Transform parent;
    private int maxSize = 100;
    private int totalCreated = 0;

    public ObjectPool(T prefab, int initialSize, Transform parent = null)
    {
        this.prefab = prefab;
        this.parent = parent;
        pool = new Stack<T>(initialSize);
        totalCreated = initialSize;

        // Initialiser le pool avec des objets désactivés
        for (int i = 0; i < initialSize; i++)
        {
            T obj = GameObject.Instantiate(prefab, parent);
            obj.gameObject.SetActive(false);
            pool.Push(obj);
        }
    }

    // Récupérer un objet du pool
    public T Get()
    {
        T obj;
        if (pool.Count > 0)
        {
            obj = pool.Pop();
        }
        else if (totalCreated < maxSize) // Add limit
        {
            obj = GameObject.Instantiate(prefab, parent);
            totalCreated++;
        }
        else
        {
            Debug.LogWarning("Pool at maximum capacity");
            return null;
        }
        obj.gameObject.SetActive(true);
        return obj;
    }

    // Retourner un objet au pool
    public void ReturnToPool(T obj)
    {
        obj.gameObject.SetActive(false);
        pool.Push(obj);
    }
}
