using UnityEngine;

namespace Gameplay
{
    // Rota este objeto en el eje Y para que siempre "mire" a la camara del jugador -- el truco
    // clasico de sprite pseudo-3D (Doom, viejos RPGs): el objeto tiene volumen real (no es un
    // quad chato), pero gira sobre si mismo para presentar siempre la misma cara de frente. Solo
    // se toca Y (nunca X/Z), asi el objeto no se inclina ni se tambalea cuando la camara mira
    // hacia arriba o abajo -- se queda parado en el piso como un cartel real.
    public class BillboardY : MonoBehaviour
    {
        private Transform _cam;

        void LateUpdate()
        {
            if (_cam == null)
            {
                if (Camera.main == null) return;
                _cam = Camera.main.transform;
            }

            Vector3 toCam = _cam.position - transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
        }
    }
}
