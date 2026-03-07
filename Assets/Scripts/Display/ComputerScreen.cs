using UnityEngine;
using UnityEngine.UI;
using TMPro;

public abstract class ComputerScreen : MonoBehaviour
{
    public string screenName; // Nom de l'écran
    public TMP_Text statusText; // Texte d'état
    public Button[] buttons; // Tableau de boutons

    protected virtual void Start()
    {
        Initialize();
    }

    // Initialisation commune
    protected virtual void Initialize()
    {
        UpdateStatus("Système prêt.");
        SetupButtonListeners(); // Configure automatiquement les listeners
    }

    // Met à jour le texte d'état
    public void UpdateStatus(string newText)
    {
        if (statusText != null)
            statusText.text = newText;
    }

    // Méthode abstraite pour gérer les clics sur les boutons
    public abstract void OnButtonClick(int buttonIndex);

    // Configure automatiquement les listeners des boutons
    protected void SetupButtonListeners()
    {
        if (buttons == null)
            return;

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i; // Capture locale pour éviter le bug de closure
            buttons[i].onClick.AddListener(() => OnButtonClick(index));
        }
    }
}