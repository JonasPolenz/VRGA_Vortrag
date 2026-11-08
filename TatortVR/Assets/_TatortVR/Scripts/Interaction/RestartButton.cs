using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TatortVR
{
    /// <summary>Button that restarts the training by reloading the scene. Pressed with grip, directly or by ray.</summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class RestartButton : MonoBehaviour
    {
        XRSimpleInteractable m_Interactable;

        void Awake()
        {
            m_Interactable = GetComponent<XRSimpleInteractable>();
            m_Interactable.selectEntered.AddListener(OnPressed);
        }

        void OnDestroy() => m_Interactable.selectEntered.RemoveListener(OnPressed);

        void OnPressed(SelectEnterEventArgs args) => Restart();

        public void Restart() => SceneManager.LoadScene(gameObject.scene.buildIndex);
    }
}
