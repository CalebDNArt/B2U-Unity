using B2U;
using System.Collections;
using UnityEngine;

public class WaveHammer : MonoBehaviour
{
    // This script is very quickly (and poorly) written, do not use it as a guide on how to animate objects.
    private Vector3 startPos;
    private Vector3 endPos;
    private Vector3 wavePos;
    private float time = 0f;
    private bool triggerWave;
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
        /**time += Time.deltaTime;
        if (time < 0.5f)
        {
            transform.position = Vector3.Lerp(startPos, endPos, time / 0.5f);
            triggerWave = true;
        }
        else if (time < 2f)
        {
            if (triggerWave)
            {
                liquidObject.InstantiateWave(wavePos, 5f);
                triggerWave = false;
            }
            transform.position = Vector3.Lerp(endPos, startPos, (time - 0.5f) / 1.5f);
        }
        else if (time < 2.1f)
        {
            transform.position = Vector3.Lerp(startPos, endPos, (time - 2f) / 0.1f);
            triggerWave = true;
        }
        else if (time < 4.1f)
        {
            if (triggerWave)
            {
                liquidObject.InstantiateWave(endPos, 10f);
                triggerWave = false;
            }
            transform.position = Vector3.Lerp(endPos, startPos, (time - 2.1f) / 2.0f);
        }
        else if (time < 5f)
        {
            transform.position = Vector3.Lerp(startPos, endPos, (time - 4.1f) / 0.9f);
            triggerWave = true;
        }
        else if (time < 7f)
        {
            if (triggerWave)
            {
                liquidObject.InstantiateWave(endPos, 2f);
                triggerWave = false;
            }
            transform.position = Vector3.Lerp(endPos, startPos, (time - 5f) / 2.0f);
        }
        else time = 0f;**/
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
