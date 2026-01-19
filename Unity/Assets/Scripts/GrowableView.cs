namespace Quantum
{
    using UnityEngine;

    public class GrowableView : QuantumEntityViewComponent
    {
        public GameObject[] Models;

        public override void OnUpdateView()
        {
            foreach (GameObject model in Models)
                model.SetActive(false);

            Models[(int)VerifiedFrame.Get<QGrowable>(EntityRef).Stage].SetActive(true);
        }
    }
}
