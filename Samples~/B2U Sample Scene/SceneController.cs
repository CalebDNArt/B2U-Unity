using B2U;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static PlayerController;
public class SceneController : MonoBehaviour
{
    public static SampleSceneInputActions actions;

    public int biomeSetting = 0; // 0 = radial, 1-8 = specific biome, 9 = randomly generated
    public B2UShaderProfile[] profiles = new B2UShaderProfile[3];
    private int activeProfile = 0; // 0 = URP, 1 = Toon, 2 = Stylized
    public B2UPaletteData[] palettes = new B2UPaletteData[4];
    private int activePalette = 0; // 0 = Fire, 1 = Ice, 2 = Poison, 3 = Tech
    public Texture2D[] radialBiomeTextures = new Texture2D[2];
    public Light mainLight;

    public bool interacting = false;
    public B2URSUVMono interactingMono = null;
    public GameObject solidRSUVUI = null;
    public B2UPaletteClicker paletteClicker = null;
    public GameObject crystalRSUVUI = null;
    public GameObject liquidRSUVUI = null;
    public TextMeshProUGUI paletteText = null;
    public TextMeshProUGUI profileText = null;
    public TextMeshProUGUI recolorText = null;
    public TextMeshProUGUI recolorTriggerText = null;
    public TextMeshProUGUI biomeText = null;

    public B2UAnimationTrigger animTrigger = null;
    private bool inLiquid = false;
    public GameObject liquidObj = null;

    private int activeRecolor = 0;
    private bool recolorEnabled = false;

    private string[] paletteNames = { "Fire", "Ice", "Poison", "Tech" };
    private string[] profileNames = { "URP", "Toon", "Stylized" };
    private string[] recolorNames = { "Thermal", "Sonar", "Rim Lighting" };

    //public int testControl = 0;

    private void OnEnable()
    {
        actions = new();
        actions.Enable();
        actions.Player.BiomeSelect.performed += OnChangeBiome;
        actions.Player.ProfileNext.performed += NextProfile;
        actions.Player.ProfileLast.performed += LastProfile;
        actions.Player.PaletteNext.performed += NextPalette;
        actions.Player.PaletteLast.performed += LastPalette;
        actions.Player.Interact.performed += SetInteractionObject;
        actions.Player.QuitInteraction.performed += OnQuitInteraction;
        actions.Player.RecolorNext.performed += NextRecolor;
        actions.Player.RecolorLast.performed += LastRecolor;
        actions.Player.RecolorToggle.performed += TriggerRecolor;
        actions.Player.QuitApp.performed += QuitGame;
    }

    private void OnDisable()
    {
        actions.Player.BiomeSelect.performed -= OnChangeBiome;
        actions.Player.ProfileNext.performed -= NextProfile;
        actions.Player.ProfileLast.performed -= LastProfile;
        actions.Player.PaletteNext.performed -= NextPalette;
        actions.Player.PaletteLast.performed -= LastPalette;
        actions.Player.Interact.performed -= SetInteractionObject;
        actions.Player.QuitInteraction.performed -= OnQuitInteraction;
        actions.Player.RecolorNext.performed -= NextRecolor;
        actions.Player.RecolorLast.performed -= LastRecolor;
        actions.Player.RecolorToggle.performed -= TriggerRecolor;
        actions.Player.QuitApp.performed -= QuitGame;
        actions.Disable();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(profiles[activeProfile]);
        B2URuntimeUtility.SetNewPalette(palettes[activePalette]);
        ChangeBiome(biomeSetting);

        paletteText.text = $"Press Q or E to change the palette (Current: {paletteNames[activePalette]})";
        profileText.text = $"Press Shift + Q or Shift + E to change the profile (Current: {profileNames[activeProfile]})";
        recolorText.text = $"Press Ctrl + Q or Ctrl + E to change the recolor effect (Current: {recolorNames[activeRecolor]})";
        biomeText.text = $"Press a number on the keyboard to change biomes (Current: Radial)";
        recolorTriggerText.text = "Press R to enable the recolor effect";

        //TESTING

        //B2URuntimeUtility.TriggerStatusEffect(testControl);
    }

    // Update is called once per frame
    void Update()
    {
        if (Physics.Raycast(PlayerController.playerPosition, Vector3.down, out RaycastHit hit, 3f))
        {
            if (B2URuntimeUtility.TryGetMaterialAtHit(hit, out B2UMaterial mat, out int variant, out bool isDynamic))
            {
                if (mat.workflow == B2UWorkflow.Liquid)
                {
                    if (!inLiquid)
                    {
                        animTrigger.TriggerAnimationEvent(activePalette);
                        inLiquid = true;
                    }
                }
                else if (inLiquid)
                {
                    animTrigger.TriggerAnimationEvent(activePalette + 4);
                    inLiquid = false;
                }
            }
        }

        mainLight.transform.rotation *= Quaternion.AngleAxis(10f * Time.deltaTime, Vector3.right);
    }

    public void QuitGame(InputAction.CallbackContext context)
    {
        if (Application.isPlaying && !Application.isEditor)
        {
            Application.Quit();
        }
    }

    public void SetInteractionObject(InputAction.CallbackContext context)
    {
        if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, 5f) && playerControl)
        {
            B2URuntimeUtility.TryGetRSUVMonoFromHit(hit, out B2URSUVMono mono);
            if (mono != null)
            {
                Cursor.lockState = CursorLockMode.None;
                interactingMono = mono;
                PlayerController.playerControl = false;
                interacting = true;

                solidRSUVUI.SetActive(mono.activeType == B2URSUVMono.RSUVType.Solid);
                crystalRSUVUI.SetActive(mono.activeType == B2URSUVMono.RSUVType.Crystal);
                liquidRSUVUI.SetActive(mono.activeType == B2URSUVMono.RSUVType.Liquid);

                switch (mono.activeType)
                {
                    case B2URSUVMono.RSUVType.Solid:
                        Transform isPaintedTrans = solidRSUVUI.transform.Find("IsPainted");
                        Toggle isPainted = isPaintedTrans.GetComponentInChildren<Toggle>();
                        isPainted.isOn = mono.solid.isPainted;
                        Transform paletteTran = solidRSUVUI.transform.Find("Palette");
                        Slider paletteSlider = paletteTran.GetComponentInChildren<Slider>();
                        paletteSlider.value = mono.solid.paletteIndex;
                        Transform emissionTransS = solidRSUVUI.transform.Find("Emission");
                        Slider emissionSliderS = emissionTransS.GetComponentInChildren<Slider>();
                        emissionSliderS.value = mono.solid.emissionStrength;
                        Transform noiseTrans = solidRSUVUI.transform.Find("Noise");
                        Slider noiseSlider = noiseTrans.GetComponentInChildren<Slider>();
                        noiseSlider.value = mono.solid.noiseOffset;
                        Transform crackTransS = solidRSUVUI.transform.Find("Cracks");
                        Slider crackSliderS = crackTransS.GetComponentInChildren<Slider>();
                        crackSliderS.value = mono.solid.crackIntensity;
                        B2UPaletteClicker clicker = solidRSUVUI.GetComponentInChildren<B2UPaletteClicker>();
                        if (clicker != null)
                        {
                            clicker.SetPalette(mono.solid.paletteIndex);
                        }
                        break;
                    case B2URSUVMono.RSUVType.Crystal:
                        Transform emissionTransC = crystalRSUVUI.transform.Find("Emission");
                        Slider emissionSliderC = emissionTransC.GetComponentInChildren<Slider>();
                        emissionSliderC.value = mono.crystal.emissionStrength;
                        Transform transmissionTrans = crystalRSUVUI.transform.Find("Transmission");
                        Slider transmissionSlider = transmissionTrans.GetComponentInChildren<Slider>();
                        transmissionSlider.value = mono.crystal.transmissionMultiplier;
                        Transform absortiptionTrans = crystalRSUVUI.transform.Find("Absorption");
                        Slider absorptionSlider = absortiptionTrans.GetComponentInChildren<Slider>();
                        absorptionSlider.value = mono.crystal.absorptionMultiplier;
                        Transform crackTrans = crystalRSUVUI.transform.Find("Cracks");
                        Slider crackSlider = crackTrans.GetComponentInChildren<Slider>();
                        crackSlider.value = mono.crystal.crackIntensity;
                        break;
                    case B2URSUVMono.RSUVType.Liquid:
                        Transform emissionTransL = liquidRSUVUI.transform.Find("Emission");
                        Slider emissionSliderL = emissionTransL.GetComponentInChildren<Slider>();
                        emissionSliderL.value = mono.liquid.emissionStrength;
                        Transform flowTrans = liquidRSUVUI.transform.Find("Flow");
                        Slider flowSlider = flowTrans.GetComponentInChildren<Slider>();
                        flowSlider.value = mono.liquid.flowStrength;
                        Transform waveTrans = liquidRSUVUI.transform.Find("Wave");
                        Slider waveSlider = waveTrans.GetComponentInChildren<Slider>();
                        waveSlider.value = mono.liquid.waveMultiplier;
                        break;
                }
            }
        }
    }

    public void TriggerRecolor(InputAction.CallbackContext context)
    {
        if (recolorEnabled)
        {
            animTrigger.TriggerAnimationEvent(8 + activeRecolor + 3);
            recolorEnabled = false;
            recolorTriggerText.text = "Press R to enable the recolor effect";
        }
        else
        {
            animTrigger.TriggerAnimationEvent(8 + activeRecolor);
            recolorEnabled = true;
            recolorTriggerText.text = "Press R to disable the recolor effect";
        }
    }

    public void OnQuitInteraction(InputAction.CallbackContext context)
    {
        QuitInteraction();
    }

    public void QuitInteraction()
    {
        if (solidRSUVUI != null) solidRSUVUI.SetActive(false);
        if (crystalRSUVUI != null) crystalRSUVUI.SetActive(false);
        if (liquidRSUVUI != null) liquidRSUVUI.SetActive(false);

        interacting = false;
        interactingMono = null;
        PlayerController.playerControl = true;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public void UI_SetPainted(bool value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Solid)
        {
            interactingMono.solid.isPainted = value;
            interactingMono.ApplyRSUV();
        }
    }

    public void UI_SetSolidPalette(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Solid)
        {
            interactingMono.solid.paletteIndex = Mathf.RoundToInt(value);
            interactingMono.ApplyRSUV();

            if (paletteClicker != null)
            {
                paletteClicker.SetPalette(value);
            }
        }
    }

    public void UI_SetSolidEmission(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Solid)
        {
            interactingMono.solid.emissionStrength = value;
            interactingMono.ApplyRSUV();
        }
    }

    public void UI_SetSolidNoiseOffset(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Solid)
        {
            interactingMono.solid.noiseOffset = Mathf.RoundToInt(value);
            interactingMono.ApplyRSUV();
        }
    }

    public void UI_SetSolidCrackIntensity(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Solid)
        {
            interactingMono.solid.crackIntensity = value;
            interactingMono.ApplyRSUV();
        }
    }

    public void UI_ToggleSolidPainted(bool isPainted)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Solid)
        {
            interactingMono.solid.isPainted = isPainted;
            interactingMono.ApplyRSUV();
        }
    }

    // Called by the Palette Clicker script
    public void UI_SetPaintIndexFromClick(Vector2 normalizedPos)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Solid)
        {
            // Map 0.0-1.0 X to 0-15 columns
            int x = Mathf.Clamp(Mathf.FloorToInt(normalizedPos.x * 16f), 0, 15);

            // Map 0.0-1.0 Y to 0-7 rows. 
            // Unity UI Rects have (0,0) at the bottom-left. Assuming visual index 0 is at the Top-Left of the image, we invert Y.
            int y = Mathf.Clamp(Mathf.FloorToInt(normalizedPos.y * 8f), 0, 7);

            int newIndex = (y * 16) + x;

            interactingMono.solid.paintIndex = newIndex;
            interactingMono.solid.isPainted = true; // Auto-enable paint mode on click
            Transform isPaintedTrans = solidRSUVUI.transform.Find("IsPainted");
            Toggle isPainted = isPaintedTrans.GetComponentInChildren<Toggle>();
            isPainted.isOn = true;
            interactingMono.ApplyRSUV();
        }
    }

    // --- LIQUID RSUV UI CALLBACKS ---
    public void UI_SetLiquidEmission(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Liquid)
        {
            interactingMono.liquid.emissionStrength = value;
            interactingMono.ApplyRSUV();
        }
    }

    public void UI_SetLiquidFlow(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Liquid)
        {
            interactingMono.liquid.flowStrength = value;
            interactingMono.ApplyRSUV();
        }
    }

    public void UI_SetLiquidWave(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Liquid)
        {
            interactingMono.liquid.waveMultiplier = value;
            interactingMono.ApplyRSUV();
        }
    }

    // --- CRYSTAL RSUV UI CALLBACKS ---
    public void UI_SetCrystalEmission(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Crystal)
        {
            interactingMono.crystal.emissionStrength = value;
            interactingMono.ApplyRSUV();
        }
    }

    public void UI_SetCrystalTransmission(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Crystal)
        {
            interactingMono.crystal.transmissionMultiplier = value;
            interactingMono.ApplyRSUV();
        }
    }

    public void UI_SetCrystalAbsorption(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Crystal)
        {
            interactingMono.crystal.absorptionMultiplier = value;
            interactingMono.ApplyRSUV();
        }
    }

    public void UI_SetCrystalCrack(float value)
    {
        if (interactingMono != null && interactingMono.activeType == B2URSUVMono.RSUVType.Crystal)
        {
            interactingMono.crystal.crackIntensity = value;
            interactingMono.ApplyRSUV();
        }
    }

    private void OnChangeBiome(InputAction.CallbackContext context)
    {
        int biomeSelection = Mathf.RoundToInt(context.ReadValue<float>());
        ChangeBiome(biomeSelection);
    }

    private void NextProfile(InputAction.CallbackContext context)
    {
        ChangeProfile(true);
    }

    private void LastProfile(InputAction.CallbackContext context)
    {
        ChangeProfile(false);
    }

    private void NextPalette(InputAction.CallbackContext context)
    {
        if (Keyboard.current.shiftKey.isPressed || Keyboard.current.ctrlKey.isPressed) return;
        ChangePalette(true);
    }

    private void LastPalette(InputAction.CallbackContext context)
    {
        if (Keyboard.current.shiftKey.isPressed || Keyboard.current.ctrlKey.isPressed) return;
        ChangePalette(false);
    }

    private void NextRecolor(InputAction.CallbackContext context)
    {
        ChangeRecolor(true);
    }

    private void LastRecolor(InputAction.CallbackContext context)
    {
        ChangeRecolor(false);
    }

    void ChangeBiome(int biome)
    {
        biomeSetting = biome;
        Texture2D b1 = null;
        Texture2D b2 = null;
        string biomeLabel = "";
        switch (biomeSetting)
        {
            case 0:
                biomeLabel = "Radial";
                b1 = radialBiomeTextures[0];
                b2 = radialBiomeTextures[1];
                break;
            case 1:
                biomeLabel = "Biome 1";
                b1 = new(1, 1);
                b1.SetPixel(0, 0, new Color(1, 0, 0, 0));
                b1.Apply();
                b2 = new(1, 1);
                b2.SetPixel(0, 0, new Color(0, 0, 0, 0));
                b2.Apply();
                break;
            case 2:
                biomeLabel = "Biome 2";
                b1 = new(1, 1);
                b1.SetPixel(0, 0, new Color(0, 1, 0, 0));
                b1.Apply();
                b2 = new(1, 1);
                b2.SetPixel(0, 0, new Color(0, 0, 0, 0));
                b2.Apply();
                break;
            case 3:
                biomeLabel = "Biome 3";
                b1 = new(1, 1);
                b1.SetPixel(0, 0, new Color(0, 0, 1, 0));
                b1.Apply();
                b2 = new(1, 1);
                b2.SetPixel(0, 0, new Color(0, 0, 0, 0));
                b2.Apply();
                break;
            case 4:
                biomeLabel = "Biome 4";
                b1 = new(1, 1);
                b1.SetPixel(0, 0, new Color(0, 0, 0, 1));
                b1.Apply();
                b2 = new(1, 1);
                b2.SetPixel(0, 0, new Color(0, 0, 0, 0));
                b2.Apply();
                break;
            case 5:
                biomeLabel = "Biome 5";
                b1 = new(1, 1);
                b1.SetPixel(0, 0, new Color(0, 0, 0, 0));
                b1.Apply();
                b2 = new(1, 1);
                b2.SetPixel(0, 0, new Color(1, 0, 0, 0));
                b2.Apply();
                break;
            case 6:
                biomeLabel = "Biome 6";
                b1 = new(1, 1);
                b1.SetPixel(0, 0, new Color(0, 0, 0, 0));
                b1.Apply();
                b2 = new(1, 1);
                b2.SetPixel(0, 0, new Color(0, 1, 0, 0));
                b2.Apply();
                break;
            case 7:
                biomeLabel = "Biome 7";
                b1 = new(1, 1);
                b1.SetPixel(0, 0, new Color(0, 0, 0, 0));
                b1.Apply();
                b2 = new(1, 1);
                b2.SetPixel(0, 0, new Color(0, 0, 1, 0));
                b2.Apply();
                break;
            case 8:
                biomeLabel = "Biome 8";
                b1 = new(1, 1);
                b1.SetPixel(0, 0, new Color(0, 0, 0, 0));
                b1.Apply();
                b2 = new(1, 1);
                b2.SetPixel(0, 0, new Color(0, 0, 0, 0));
                b2.Apply();
                break;
            case 9:
                biomeLabel = "Random (Press 9 to regenerate)";
                b1 = new Texture2D(256, 256);
                Color32[] colors1 = new Color32[65536];
                b2 = new Texture2D(256, 256);
                Color32[] colors2 = new Color32[65536];

                float rOffsetX = UnityEngine.Random.Range(0f, 1000f);
                float rOffsetY = UnityEngine.Random.Range(0f, 1000f);

                for (int i = 0; i < colors1.Length; i++)
                {
                    int xInt = i % 256;
                    int yInt = i / 256;

                    float x = ((float)xInt / 256f) * 8f + rOffsetX;
                    float y = ((float)yInt / 256f) * 8f + rOffsetY;

                    float w1 = Mathf.Pow(Mathf.PerlinNoise(x, y), 6f);
                    float w2 = Mathf.Pow(Mathf.PerlinNoise(x + 13.5f, y - 8.2f), 6f);
                    float w3 = Mathf.Pow(Mathf.PerlinNoise(x - 22.1f, y + 17.8f), 6f);
                    float w4 = Mathf.Pow(Mathf.PerlinNoise(x + 7.3f, y + 33.4f), 6f);
                    float w5 = Mathf.Pow(Mathf.PerlinNoise(x - 41.9f, y - 11.1f), 6f);
                    float w6 = Mathf.Pow(Mathf.PerlinNoise(x + 28.4f, y - 29.6f), 6f);
                    float w7 = Mathf.Pow(Mathf.PerlinNoise(x - 19.3f, y + 42.7f), 6f);

                    float totalWeight = w1 + w2 + w3 + w4 + w5 + w6 + w7 + 0.0001f;

                    colors1[i].r = (byte)((w1 / totalWeight) * 255f);
                    colors1[i].g = (byte)((w2 / totalWeight) * 255f);
                    colors1[i].b = (byte)((w3 / totalWeight) * 255f);
                    colors1[i].a = (byte)((w4 / totalWeight) * 255f);

                    colors2[i].r = (byte)((w5 / totalWeight) * 255f);
                    colors2[i].g = (byte)((w6 / totalWeight) * 255f);
                    colors2[i].b = (byte)((w7 / totalWeight) * 255f);
                    colors2[i].a = 0;
                }

                b1.SetPixels32(colors1);
                b1.Apply();
                b2.SetPixels32(colors2);
                b2.Apply();
                break;
        }

        if (b1 != null) B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.BiomeTexture1, b1);
        if (b2 != null) B2URuntimeUtility.SetB2UShaderPropertyValue(B2UShaderProperty.BiomeTexture2, b2);

        biomeText.text = $"Press a number on the keyboard to change biomes (Current: {biomeLabel})";
    }

    void ChangePalette(bool fwd)
    {
        int targetPalette = fwd ? activePalette + 1 : activePalette - 1;
        if (targetPalette == palettes.Length)
        {
            targetPalette = 0;
        }
        else if (targetPalette == -1)
        {
            targetPalette = palettes.Length - 1;
        }
        activePalette = targetPalette;
        paletteText.text = $"Press Q or E to change the palette (Current: {paletteNames[activePalette]})";
        B2URuntimeUtility.SetNewPalette(palettes[targetPalette]);
    }

    void ChangeProfile(bool fwd)
    {
        int targetProfile = fwd ? activeProfile + 1 : activeProfile - 1;
        if (targetProfile == profiles.Length)
        {
            targetProfile = 0;
        }
        else if (targetProfile == -1)
        {
            targetProfile = profiles.Length - 1;
        }
        activeProfile = targetProfile;
        profileText.text = $"Press Shift + Q or Shift + E to change the profile (Current: {profileNames[activeProfile]})";
        B2URuntimeUtility.SetB2UShaderPropertyValuesFromProfile(profiles[targetProfile]);
        B2URuntimeUtility.SetNewPalette(palettes[activePalette]);
    }

    void ChangeRecolor(bool fwd)
    {
        int targetRecolor = fwd ? activeRecolor + 1 : activeRecolor - 1;
        if (targetRecolor == 3)
        {
            targetRecolor = 0;
        }
        else if (targetRecolor == -1)
        {
            targetRecolor = 2;
        }
        activeRecolor = targetRecolor;
        recolorText.text = $"Press Ctrl + Q or Ctrl + E to change the recolor effect (Current: {recolorNames[activeRecolor]})";
    }
}
