using System.Collections.Generic;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    public int Health;
    public List<string> Inventory = new List<string>();

    public void ApplyData(int health, List<string> inventory)
    {
        Health = health;
        Inventory = new List<string>(inventory);
    }

    public void SaveData()
    {
        GameDataManager.Instance.Health = Health;
        GameDataManager.Instance.Inventory = new List<string>(Inventory);
    }
}