using System.Collections.Generic;
using UnityEngine;

public class ObjectPool<T> where T : Component
{
    private Stack<T> pool;
    private T prefab;
    private Transform parent;

    public ObjectPool(T prefab, int initialSize, Transform parent = null)
    {
        this.prefab = prefab;
        this.parent = parent;
        pool = new Stack<T>(initialSize);

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
        else
        {
            // Si le pool est vide, créer un nouvel objet
            obj = GameObject.Instantiate(prefab, parent);
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
