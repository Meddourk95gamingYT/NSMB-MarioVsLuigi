using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using TMPro;

public class UIUpdater : MonoBehaviour {
    
    public static UIUpdater Instance;
    public GameObject playerTrackTemplate, starTrackTemplate;
    PlayerController player;
    //TODO: refactor
    public Sprite storedItemNull, storedItemMushroom, storedItemFireFlower, storedItemMiniMushroom, storedItemMegaMushroom, storedItemBlueShell, storedItemPropellerMushroom; 
    public TMP_Text uiStars, uiCoins, uiDebug, uiLives;
    public Image itemReserve;
    private float pingSample = 0, fpsSample = 0;

    void Start() {
        Instance = this;
        pingSample = PhotonNetwork.GetPing();
    }
    
    public void GivePlayersIcons() {
        foreach (GameObject player in GameObject.FindGameObjectsWithTag("Player")) {
            GameObject trackObject = Instantiate(playerTrackTemplate, playerTrackTemplate.transform.position, Quaternion.identity, transform);
            TrackIcon icon = trackObject.GetComponent<TrackIcon>();
            icon.target = player;

            if (!player.GetPhotonView().IsMine)
                trackObject.transform.localScale = new Vector3(2f/3f, 2f/3f, 1f);

            trackObject.SetActive(true);
        }
    }

    void Update() {
        float newPing = PhotonNetwork.GetPing();
        pingSample = Mathf.Lerp(pingSample, newPing, 0.01f);

        float newFPS = 1.0f / Time.smoothDeltaTime;
        fpsSample = Mathf.Lerp(fpsSample, newFPS, 0.01f);

        uiDebug.text = $"{(int) fpsSample}FPS | Ping: {(int) pingSample}ms";
        
        //Player stuff update.
        if (!player && GameManager.Instance.localPlayer)
            player = GameManager.Instance.localPlayer.GetComponent<PlayerController>();

        UpdateStoredItemUI();
        UpdateTextUI();
    }
    
    void UpdateStoredItemUI() {
        if (!player)
            return;

        //TODO: refactor
        itemReserve.sprite = player.storedPowerup switch {
            "Mushroom" => storedItemMushroom,
            "FireFlower" => storedItemFireFlower,
            "MiniMushroom" => storedItemMiniMushroom,
            "MegaMushroom" => storedItemMegaMushroom,
            "BlueShell" => storedItemBlueShell,
            "PropellerMushroom" => storedItemPropellerMushroom,
            _ => storedItemNull,
        };
    }
    void UpdateTextUI() {
        if (!player)
            return;

        uiStars.text = "<sprite=0>" + player.stars + "/" + GameManager.Instance.starRequirement;
        uiCoins.text = "<sprite=1>" + player.coins + "/8";
        uiLives.text = (player.lives > 0 ? (Utils.GetCharacterIndex(player.photonView.Owner) == 0 ? "<sprite=3>" : "<sprite=4>") + player.lives : "");
    }
}
