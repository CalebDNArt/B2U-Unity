using B2U;
using System.Collections;
using UnityEngine;

public class WaveHammer : MonoBehaviour
{
    // This script is very quickly (and poorly) written, do not use it as a guide on how to animate objects.
    private Vector3 startPos;
    private Vector3 endPos;
    private Vector3 wavePos;
    public GameObject liquidObject;

    public GameObject liquidDrawObject;
    public GameObject crystalObject;

    void Start()
    {
        startPos = transform.position;
        endPos = new Vector3(startPos.x, startPos.y - 3.5f, startPos.z);
        wavePos = new Vector3(endPos.x, endPos.y - 2.5f, endPos.z);
        StartCoroutine(StartWaveHammer());
    }

    void Update()
    {
        float angleStep = 50f * Time.deltaTime;
        liquidDrawObject.transform.rotation = Quaternion.AngleAxis(5f * angleStep, Vector3.up) * liquidDrawObject.transform.rotation;
        crystalObject.transform.rotation = Quaternion.AngleAxis(angleStep, Vector3.up) * crystalObject.transform.rotation;
    }

    IEnumerator StartWaveHammer()
    {
        while (true)
        {
            float t = 0f;
            bool waveTriggered = false;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(startPos, endPos, t / 0.5f);
                if (t > 0.25f && !waveTriggered)
                {
                    liquidObject.InstantiateWave(wavePos, 5f);
                    waveTriggered = true;
                }
                yield return null;
            }
            while (t < 2f)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(endPos, startPos, (t - 0.5f) / 1.5f);
                yield return null;
            }
            yield return null;
        }
    }
}
