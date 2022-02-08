using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.InputSystem;
using Photon.Pun;

public class PlayerController : MonoBehaviourPun, IPunObservable {
    
    private static int ANY_GROUND_MASK, ONLY_GROUND_MASK, GROUND_LAYERID, HITS_NOTHING_LAYERID, DEFAULT_LAYERID;
    
    private int playerId = 0;
    public bool dead = false;
    public Enums.PowerupState state = Enums.PowerupState.Small, previousState;
    public float slowriseGravity = 0.85f, normalGravity = 2.5f, flyingGravity = 0.8f, flyingTerminalVelocity = 1.25f, drillVelocity = 7f, deathUpTime = 0.6f, deathForce = 7f, groundpoundTime = 0.25f, groundpoundVelocity = 12, blinkingSpeed = 0.25f, terminalVelocity = -7f, jumpVelocity = 6.25f, launchVelocity = 12f, walkingAcceleration = 8f, runningAcceleration = 3f, walkingMaxSpeed = 2.7f, runningMaxSpeed = 5, wallslideSpeed = -4.25f, walljumpVelocity = 5.6f, pipeDuration = 2f, giantStartTime = 1.5f, blinkDuration = 0.1f;
    [SerializeField] ParticleSystem dust, sparkles, drillParticle, giantParticle;
    private BoxCollider2D[] hitboxes;
    GameObject models;

    public CameraController cameraController;
    
    private AudioSource sfx;
    private Animator animator;
    public Rigidbody2D body;

    public bool onGround, crushGround, doGroundSnap, onRight, onLeft, hitRoof, skidding, turnaround, facingRight = true, singlejump, doublejump, triplejump, bounce, crouching, groundpound, sliding, knockback, deathUp, hitBlock, running, functionallyRunning, jumpHeld, flying, drill, inShell, hitLeft, hitRight, iceSliding, stuckInBlock;
    public float walljumping, landing, koyoteTime, deathCounter, groundpoundCounter, groundpoundDelay, hitInvincibilityCounter, powerupFlash, throwInvincibility, jumpBuffer, pipeTimer, giantStartTimer, giantEndTimer;
    public float invincible, giantTimer, blinkTimer, fadeOutTimer, fadeInTimer, floorAngle;
    
    private Vector2 pipeDirection;
    public int stars, coins;
    public string storedPowerup = null;
    public HoldableEntity holding, holdingOld;
    [ColorUsage(true, false)]
    public Color glowColor = Color.clear;


    private readonly float analogDeadzone = 0.35f;
    public Vector2 joystick, savedVelocity;

    public GameObject smallModel, largeModel, blueShell;
    public Avatar smallAvatar, largeAvatar;
    public GameObject onSpinner;
    PipeManager pipeEntering;
    private Vector3 cameraOffsetLeft = Vector3.left, cameraOffsetRight = Vector3.right, cameraOffsetZero = Vector3.zero;
    private bool starDirection, step, alreadyGroundpounded, wasTurnaround;
    private Enums.PlayerEyeState eyeState;
    public PlayerData character;
    public float heightSmallModel = 0.46f, heightLargeModel = 0.82f;

    //Tile data
    private string footstepMaterial = "";
    private bool doIceSkidding;
    private float tileFriction = 1;
    private readonly HashSet<Vector3Int> tilesStandingOn = new(), 
        tilesJumpedInto = new(), 
        tilesHitSide = new();
    

    private long localFrameId = 0;
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info) {
        if (stream.IsWriting) {

            stream.SendNext(body.position);
            stream.SendNext(body.velocity);

            ExitGames.Client.Photon.Hashtable controls = new()
            {
                ["joystick"] = joystick,
                ["sprintHeld"] = running,
                ["jumpHeld"] = jumpHeld
            };

            stream.SendNext(controls);
            stream.SendNext(localFrameId++);

        } else if (stream.IsReading) {
            Vector2 pos = (Vector2) stream.ReceiveNext();
            Vector2 vel = (Vector2) stream.ReceiveNext();
            ExitGames.Client.Photon.Hashtable controls = (ExitGames.Client.Photon.Hashtable) stream.ReceiveNext();
            long frameId = (long) stream.ReceiveNext();

            if (frameId < localFrameId) {
                //recevied info older than what we have
                return;
            }
            float lag = (float) (PhotonNetwork.Time - info.SentServerTime);

            // if (Vector3.Distance(pos, body.position) > 15 * lag) {
            //     Debug.Log("distance off by " + Vector3.Distance(pos, body.position));
            // }
            body.position = pos;
            body.velocity = vel;
            localFrameId = frameId;

            joystick = (Vector2) controls["joystick"]; 
            running = (bool) controls["sprintHeld"];
            jumpHeld = (bool) controls["jumpHeld"];

            // Debug.Log(lag + " -> " + (int) (lag*1000) + "ms");
            HandleMovement(lag);
            // body.position += (Vector3) (vel * lag);
        }
    }

    public void Awake() {
        ANY_GROUND_MASK = LayerMask.GetMask("Ground", "Semisolids");
        ONLY_GROUND_MASK = LayerMask.GetMask("Ground");
        GROUND_LAYERID = LayerMask.NameToLayer("Ground");
        HITS_NOTHING_LAYERID = LayerMask.NameToLayer("HitsNothing");
        DEFAULT_LAYERID = LayerMask.NameToLayer("Default");
        
        cameraController = GetComponent<CameraController>();
        animator = GetComponentInChildren<Animator>();
        body = GetComponent<Rigidbody2D>();
        sfx = GetComponent<AudioSource>();
        models = transform.Find("Models").gameObject;
        // if (photonView.IsMine) {
        //     cameraMask = GameObject.FindGameObjectWithTag("CanvasMask");
        //     if (cameraMask)
        //         cameraMask.transform.localScale = Vector3.zero;
        // }
        starDirection = Random.value < 0.5;
        PlayerInput input = GetComponent<PlayerInput>();
        input.enabled = !photonView || photonView.IsMine;
        
        smallModel.SetActive(false);
        largeModel.SetActive(false);

        if (photonView) {
            playerId = System.Array.IndexOf(PhotonNetwork.PlayerList, photonView.Owner);
            if (!photonView.IsMine) {
                glowColor = Color.HSVToRGB((float) playerId / ((float) PhotonNetwork.PlayerList.Length + 1), 1, 1);
            }
        }
    }
    public void Start() {
        hitboxes = GetComponents<BoxCollider2D>();
    }
    
    void HandleGroundCollision() {
        tilesJumpedInto.Clear();
        tilesStandingOn.Clear();
        tilesHitSide.Clear();

        bool ignoreRoof = false;
        int down = 0, left = 0, right = 0, up = 0;

        crushGround = false;
        foreach (BoxCollider2D hitbox in hitboxes) {
            ContactPoint2D[] contacts = new ContactPoint2D[20];
            int collisionCount = hitbox.GetContacts(contacts);

            for (int i = 0; i < collisionCount; i++) {
                ContactPoint2D contact = contacts[i];
                Vector2 n = contact.normal;
                Vector2 p = contact.point + (contact.normal * -0.15f);
                if (n == Vector2.up && contact.point.y > body.position.y) continue;
                Vector3Int vec = Utils.WorldToTilemapPosition(p);
                if (contact.collider.CompareTag("Player")) continue;

                if (Vector2.Dot(n,Vector2.up) > .05f) {
                    if (Vector2.Dot(body.velocity.normalized, n) > 0.1f && !onGround) {
                        //invalid flooring
                        continue;
                    }
                    crushGround |= !contact.collider.gameObject.CompareTag("platform");
                    down++;
                    tilesStandingOn.Add(vec );
                } else if (contact.collider.gameObject.layer == GROUND_LAYERID) {
                    if (Vector2.Dot(n,Vector2.left) > .9f) {
                        right++;
                        tilesHitSide.Add(vec);
                    } else if (Vector2.Dot(n,Vector2.right) > .9f) {
                        left++;
                        tilesHitSide.Add(vec);
                    } else if (Vector2.Dot(n,Vector2.down) > .9f && !groundpound) {
                        up++;
                        tilesJumpedInto.Add(vec);
                    }
                } else {
                    ignoreRoof = true;
                }
            }
        }

        onGround = down >= 1;
        hitLeft = left >= 2;
        onLeft = hitLeft && !inShell && body.velocity.y < -0.1 && !facingRight && !onGround && !holding && state != Enums.PowerupState.Giant && !flying;
        hitRight = right >= 2;
        onRight = hitRight && !inShell && body.velocity.y < -0.1 && facingRight && !onGround && !holding && state != Enums.PowerupState.Giant && !flying;
        hitRoof = !ignoreRoof && !onLeft && !onRight && up >= 2 && body.velocity.y > -0.2f;
    }

    public void OnCollisionEnter2D(Collision2D collision) {
        if (!photonView.IsMine)
            return;
        
        if (collision.gameObject.CompareTag("Player")) {
            //hit antoher player
            foreach (ContactPoint2D contact in collision.contacts) {
                GameObject otherObj = collision.gameObject;
                PlayerController other = otherObj.GetComponent<PlayerController>();
                PhotonView otherView = other.photonView;

                if (other.animator.GetBool("invincible")) {
                    return;
                }
                if (invincible > 0) {
                    otherView.RPC("Powerdown", RpcTarget.All, false);
                    return;
                }

                if (state == Enums.PowerupState.Giant) {
                    if (other.state == Enums.PowerupState.Giant) {
                        otherView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x < body.position.x, 0, -1);
                        photonView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x > body.position.x, 0, -1);
                    } else {
                        otherView.RPC("Powerdown", RpcTarget.All, false);
                    }
                    return;
                }

                if (contact.normal.y > 0) {
                    //hit them from above
                    bounce = !groundpound;
                    drill = false;
                    if (state == Enums.PowerupState.Mini && !groundpound) {
                        photonView.RPC("PlaySound", RpcTarget.All, "enemy/shell_kick");
                    } else if (other.state == Enums.PowerupState.Mini && groundpound) {
                        otherView.RPC("Powerdown", RpcTarget.All, false);
                        bounce = false;
                    } else {
                        otherView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x < body.position.x, groundpound && state != Enums.PowerupState.Mini ? 2 : 1, photonView.ViewID);
                    }
                    return;
                }

                if (inShell) {
                    if (other.inShell) {
                        otherView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x < body.position.x, 0, -1);
                        photonView.RPC("Knockback", RpcTarget.All, otherObj.transform.position.x > body.position.x, 0, -1);
                    } else {
                        otherView.RPC("Powerdown", RpcTarget.All, false);
                    }
                    return;
                }

            }
            return;
        }
    }
    protected void GiantFootstep() {
        cameraController.screenShakeTimer = 0.15f;
        SpawnParticle("Prefabs/Particle/GroundpoundDust", body.position.x + (facingRight ? 0.5f : -0.5f), body.position.y);
        //TODO: find stomp sound
    }
    protected void Footstep() {
        if (state == Enums.PowerupState.Giant) return;
        if (doIceSkidding && skidding) {
            PlaySoundFromAnim("player/ice-skid");
            return;
        }
        if (Mathf.Abs(body.velocity.x) < walkingMaxSpeed) return;
        
        PlaySoundFromAnim("player/walk" + (footstepMaterial != "" ? "-" + footstepMaterial : "") + (step ? "-2" : ""), Mathf.Abs(body.velocity.x) / (runningMaxSpeed + 4));
        step = !step;
    }

    #region Controls Callbacks
    protected void OnMovement(InputValue value) {
        if (!photonView.IsMine) return;
        joystick = value.Get<Vector2>();
    }

    protected void OnJump(InputValue value) {
        if (!photonView.IsMine) return;
        jumpHeld = value.Get<float>() >= 0.5f;
        if (jumpHeld) {
            jumpBuffer = 0.15f;
        }
    }

    protected void OnSprint(InputValue value) {
        if (!photonView.IsMine) return;
        running = value.Get<float>() >= 0.5f;
    }

    protected void OnFireball() {
        if (!photonView.IsMine) return;
        if (GameManager.Instance.paused) return;
        if (crouching || sliding) return;
        if (onLeft || onRight) return;
        if (groundpound || knockback) return;
        if (state != Enums.PowerupState.FireFlower) return;
        if (dead || triplejump || holding || flying || drill) return;
        if (GameManager.Instance.gameover) return;
        if (pipeEntering) return;

        int count = 0;
        foreach (FireballMover existingFire in GameObject.FindObjectsOfType<FireballMover>()) {
            if (existingFire.photonView.IsMine) {
                if (++count >= 2) 
                    return;
            }
        }

        PhotonNetwork.Instantiate("Prefabs/Fireball", body.position + new Vector2(facingRight ? 0.3f : -0.3f, 0.4f), Quaternion.identity, 0, new object[]{!facingRight});
        photonView.RPC("PlaySound", RpcTarget.All, "player/fireball");
        animator.SetTrigger("fireball");
    }

    protected void OnItem() {
        if (!photonView.IsMine) return;
        if (GameManager.Instance.paused) return;
        if (dead) return;
        if (storedPowerup == null || storedPowerup.Length <= 0) return; 

        SpawnItem(storedPowerup);
        storedPowerup = null;
    }

    protected void OnPause() {
        if (!photonView.IsMine) return;
        PlaySoundFromAnim("pause");
        GameManager.Instance.Pause();
    }
    #endregion

    [PunRPC]
    protected void HoldingWakeup() {
        holding = null;
        holdingOld = null;
        throwInvincibility = 0;
        Powerdown(false);
    }

    [PunRPC]
    protected void Powerup(int actor, string powerup, int powerupViewId) {
        if (!PhotonView.Find(actor))
            return;
        bool stateUp = false;
        Enums.PowerupState previous = state;
        string powerupSfx = "powerup";
        string store = null;
        switch (powerup) {
        case "Mushroom": {
            if (state <= Enums.PowerupState.Small) {
                state = Enums.PowerupState.Large;
                stateUp = true;
                transform.localScale = Vector3.one;
            } else if (storedPowerup == null || storedPowerup == "") {
                store = powerup;
            }
            break;
        }
        case "FireFlower": {
            if (state != Enums.PowerupState.Giant && state != Enums.PowerupState.FireFlower) {
                state = Enums.PowerupState.FireFlower;
                stateUp = true;
                transform.localScale = Vector3.one;
            } else {
                store = powerup;
            }
            break;
        }
        case "Star": {
            invincible = 10f;
            stateUp = true;
            break;
        }
        case "MiniMushroom": {
            if (state == Enums.PowerupState.Mini || state == Enums.PowerupState.Giant) {
                store = powerup;
            } else {
                stateUp = true;
                state = Enums.PowerupState.Mini;
                transform.localScale = Vector3.one / 2;
                powerupSfx = "powerup-mini";
            }
            break;
        }
        case "BlueShell": {
            if (state == Enums.PowerupState.Shell || state == Enums.PowerupState.Giant) {
                store = powerup;
            } else {
                stateUp = true;
                state = Enums.PowerupState.Shell;
                transform.localScale = Vector3.one;
            }
            break;
        }
        case "MegaMushroom": {
            if (state == Enums.PowerupState.Giant) {
                store = powerup;
                break;
            }
            state = Enums.PowerupState.Giant;
            stateUp = true;
            powerupSfx = "powerup-mega";
            giantStartTimer = giantStartTime;
            groundpound = false;
            crouching = false;
            giantTimer = 15f;
            transform.localScale = Vector3.one;
            GameObject.Instantiate(Resources.Load("Prefabs/Particle/GiantPowerup"), (Vector3) body.position + (Vector3.forward * transform.position.z), Quaternion.identity);

            break;
        }
        }
        if (store != null) {
            storedPowerup = store;
        }
        if (stateUp) {
            previousState = previous;
            PlaySoundFromAnim("player/" + powerupSfx);
            powerupFlash = 2f;
            crouching |= ForceCrouchCheck();
        } else {
            PlaySoundFromAnim("player/reserve_item_store");
        }

        PhotonView view = PhotonView.Find(powerupViewId);
        if (view.IsMine) {
            PhotonNetwork.Destroy(view);
        } else {
            Destroy(view.gameObject);
        }
    }

    [PunRPC]
    protected void Powerdown(bool ignoreInvincible = false) {
        if (!ignoreInvincible && hitInvincibilityCounter > 0)
            return;

        previousState = state;

        switch (state) {
        case Enums.PowerupState.Mini:
        case Enums.PowerupState.Small: {
            Death(false);
            break;
        }
        case Enums.PowerupState.Large: {
            state = Enums.PowerupState.Small;
            powerupFlash = 2f;
            SpawnStar();
            break;
        }
        case Enums.PowerupState.FireFlower:
        case Enums.PowerupState.Shell: {
            state = Enums.PowerupState.Large;
            powerupFlash = 2f;
            SpawnStar();
            break;
        }
        }

        if (!dead) {
            hitInvincibilityCounter = 3f;
            PlaySoundFromAnim("player/powerdown");
        }
    }

    [PunRPC]
    protected void SetCoins(int coins) {
        this.coins = coins;
    }
    [PunRPC]
    protected void SetStars(int stars) {
        this.stars = stars;
    }
    protected void OnTriggerEnter2D(Collider2D collider) {
        if (dead) return;
        if (!photonView.IsMine) return;

        HoldableEntity holdable = collider.gameObject.GetComponentInParent<HoldableEntity>();
        if (holdable && (holding == holdable || (holdingOld == holdable && throwInvincibility > 0))) return;
        KillableEntity killable = collider.gameObject.GetComponentInParent<KillableEntity>();
        if (killable && !killable.dead) {
            killable.InteractWithPlayer(this);
            return;
        }

        GameObject obj = collider.gameObject;
        switch (obj.tag) {
            case "bigstar": {
                photonView.RPC("CollectBigStar", RpcTarget.AllViaServer, obj.transform.parent.gameObject.GetPhotonView().ViewID);
                break;
            }
            case "loosecoin": {
                Transform parent = obj.transform.parent;
                photonView.RPC("CollectCoin", RpcTarget.AllViaServer, parent.gameObject.GetPhotonView().ViewID, parent.position.x, parent.position.y);
                break;
            }
            case "coin": {
                photonView.RPC("CollectCoin", RpcTarget.All, obj.GetPhotonView().ViewID, obj.transform.position.x, collider.transform.position.y);
                break;
            }
            case "Fireball": {
                FireballMover fireball = obj.GetComponentInParent<FireballMover>();
                if (fireball.photonView.IsMine)
                    break;
                fireball.photonView.RPC("Kill", RpcTarget.All);
                if (state == Enums.PowerupState.Shell && (inShell || crouching || groundpound))
                    break;
                if (state == Enums.PowerupState.Mini) {
                    photonView.RPC("Powerdown", RpcTarget.All, false);
                } else {
                    photonView.RPC("Knockback", RpcTarget.All, collider.attachedRigidbody.position.x > body.position.x, 1, fireball.photonView.ViewID);
                }
                break;
            }
        }
    }
    protected void OnTriggerStay2D(Collider2D collider) {
        GameObject obj = collider.gameObject;
        switch (obj.tag) {
            case "spinner":
                onSpinner = obj;
                break;
            case "BlueShell":
            case "Star":
            case "MiniMushroom":
            case "FireFlower":
            case "MegaMushroom":
            case "Mushroom": {
                if (!photonView.IsMine) return;
                MovingPowerup powerup = obj.GetComponentInParent<MovingPowerup>();
                if (powerup.followMeCounter > 0 || powerup.ignoreCounter > 0)
                    break;
                photonView.RPC("Powerup", RpcTarget.AllViaServer, powerup.photonView.ViewID, obj.tag, obj.transform.parent.gameObject.GetPhotonView().ViewID);
                Destroy(collider);
                break;
            }
            case "poison": {
                if (!photonView.IsMine) return;
                photonView.RPC("Death", RpcTarget.All, false);
                break;
            }
        }
    }
    protected void OnTriggerExit2D(Collider2D collider) {
        switch (collider.tag) {
            case "spinner": {
                onSpinner = null;
                break;
            }
        }
    }

    [PunRPC]
    protected void CollectBigStar(int starID) {
        PhotonView view = PhotonView.Find(starID);
        if (view == null) return;
        GameObject star = view.gameObject;
        StarBouncer starScript = star.GetComponent<StarBouncer>();
        if (starScript.readyForUnPassthrough > 0 && starScript.creator == photonView.ViewID) return;
        
        if (photonView.IsMine) {
            photonView.RPC("SetStars", RpcTarget.Others, ++stars);
        }
        if (starScript.stationary) {
            //Main star, reset the tiles.
            GameManager.Instance.ResetTiles();
        }
        GameObject.Instantiate(Resources.Load("Prefabs/Particle/StarCollect"), star.transform.position, Quaternion.identity);
        PlaySoundFromAnim("player/star_collect");
        if (view.IsMine) {
            PhotonNetwork.Destroy(view);
        }
    }

    [PunRPC]
    protected void CollectCoin(int coinID, float x, float y) {
        if (PhotonView.Find(coinID)) {
            GameObject coin = PhotonView.Find(coinID).gameObject;
            if (coin.CompareTag("loosecoin")) {
                if (coin.GetPhotonView().IsMine) {
                    PhotonNetwork.Destroy(coin);
                }
            } else {
                SpriteRenderer renderer = coin.GetComponent<SpriteRenderer>();
                if (!renderer.enabled)
                    return;
                renderer.enabled = false;
                coin.GetComponent<BoxCollider2D>().enabled = false;
            }
            GameObject.Instantiate(Resources.Load("Prefabs/Particle/CoinCollect"), new Vector3(x, y, 0), Quaternion.identity);
        }

        coins++;

        PlaySoundFromAnim("player/coin");
        GameObject num = (GameObject) GameObject.Instantiate(Resources.Load("Prefabs/Particle/Number"), new Vector3(x, y, 0), Quaternion.identity);
        Animator anim = num.GetComponentInChildren<Animator>();
        anim.SetInteger("number", coins);
        anim.SetTrigger("ready");

        if (photonView.IsMine) {
            if (coins >= 8) {
                SpawnItem();
                coins = 0;
            }
            photonView.RPC("SetCoins", RpcTarget.Others, coins);
        }
    }

    void SpawnItem(string item = null) {
        if (item == null) {
            item = Utils.GetRandomItem(stars).prefab;
        }

        PhotonNetwork.Instantiate("Prefabs/Powerup/" + item, body.position + new Vector2(0, 5), Quaternion.identity, 0, new object[]{photonView.ViewID});
        photonView.RPC("PlaySound", RpcTarget.All, "player/reserve_item_use");
    }



    void SpawnStar() {
        if (stars <= 0) return;
        stars--;
        if (!PhotonNetwork.IsMasterClient) return;

        GameObject star = PhotonNetwork.InstantiateRoomObject("Prefabs/BigStar", transform.position, Quaternion.identity, 0, new object[]{starDirection, photonView.ViewID});
        StarBouncer sb = star.GetComponent<StarBouncer>();
        sb.photonView.TransferOwnership(PhotonNetwork.MasterClient);
        photonView.RPC("SetStars", RpcTarget.Others, stars);
        starDirection = !starDirection;
    }
    [PunRPC]
    protected void Death(bool deathplane) {
        dead = true;

        onSpinner = null;
        pipeEntering = null;
        flying = false;
        drill = false;
        animator.SetBool("flying", false);
        deathCounter = 0;
        onLeft = false;
        onRight = false;
        skidding = false;
        turnaround = false;
        inShell = false;
        knockback = false;
        animator.Play("deadstart", state >= Enums.PowerupState.Large ? 1 : 0);
        PlaySoundFromAnim("player/death");
        SpawnStar();
        if (holding) {
            holding.photonView.RPC("Throw", RpcTarget.All, !facingRight, true);
            holding = null;
        }
        if (deathplane) {
            body.position += Vector2.down * 20;
            transform.position += Vector3.down * 20;
        }
    }

    [PunRPC]
    public void PreRespawn() {
        cameraController.currentPosition = transform.position = body.position = GameManager.Instance.GetSpawnpoint(playerId);
        gameObject.layer = DEFAULT_LAYERID;
        cameraController.scrollAmount = 0;
        cameraController.Update();
        state = Enums.PowerupState.Small;
        dead = false;
        animator.SetTrigger("respawn");
        invincible = 0;
        giantTimer = 0;
        giantEndTimer = 0;
        giantStartTimer = 0;

        GameObject particle = (GameObject) Instantiate(Resources.Load("Prefabs/Particle/Respawn"), body.position, Quaternion.identity);
        if (photonView.IsMine) {
            particle.GetComponent<RespawnParticle>().player = this;
        }
        gameObject.SetActive(false);
    }

    [PunRPC]
    public void Respawn() {
        gameObject.SetActive(true);
        dead = false;
        state = Enums.PowerupState.Small;
        previousState = Enums.PowerupState.Small;
        if (body)
            body.velocity = Vector2.zero;
        onLeft = false;
        onRight = false;
        flying = false;
        crouching = false;
        onGround = false;
        jumpBuffer = 0;
        SetParticleEmission(dust, false);
        SetParticleEmission(sparkles, false);
        SetParticleEmission(drillParticle, false);
        SetParticleEmission(giantParticle, false);
        invincible = 0;
        giantStartTimer = 0;
        giantTimer = 0;
        singlejump = false;
        doublejump = false;
        turnaround = false;
        triplejump = false;
        knockback = false;
        bounce = false;
        skidding = false;
        walljumping = 0f;
        groundpound = false;
        inShell = false;
        landing = 0f;
        ResetKnockback();
        Instantiate(Resources.Load("Prefabs/Particle/Puff"), transform.position, Quaternion.identity);
    }

    [PunRPC]
    protected void PlaySound(string sound) {
        sfx.PlayOneShot((AudioClip) Resources.Load("Sound/" + sound));
    }

    [PunRPC]
    protected void SpawnParticle(string particle) {
        Instantiate(Resources.Load(particle), transform.position, Quaternion.identity);
    }

    [PunRPC]
    protected void SpawnParticle(string particle, float x, float y) {
        Instantiate(Resources.Load(particle), new Vector2(x, y), Quaternion.identity);
    }
    
    [PunRPC]
    protected void SpawnParticle(string particle, float x, float y, Vector3 rot) {
        Instantiate(Resources.Load(particle), new Vector2(x, y), Quaternion.Euler(rot));
    }

    void HandleGiantTiles(bool pipes) {
        int minY = (singlejump && onGround) ? 0 : 1, maxY = Mathf.Abs(body.velocity.y) > 0.05f ? 8 : 7;
        Vector2 offset = (Vector2.right * 0.3f) * (facingRight ? 1 : -1);
        int width = 1;
        if (groundpound) {
            offset = new Vector2(0, -0.3f);
        }
        for (int x = -width; x <= width; x++) {
            for (int y = minY; y <= maxY; y++) {
                Vector3Int tileLocation = Utils.WorldToTilemapPosition(body.position + offset + new Vector2(x/2f, y/2f - 0.4f));

                BreakablePipeTile pipe = GameManager.Instance.tilemap.GetTile<BreakablePipeTile>(tileLocation);
                if (pipe && (pipe.upsideDownPipe || !pipes)) continue;

                InteractableTile.InteractionDirection dir = InteractableTile.InteractionDirection.Up;
                if (y == minY && singlejump && onGround) {
                    dir = InteractableTile.InteractionDirection.Down;
                } else if (x == -width) {
                    dir = InteractableTile.InteractionDirection.Left;
                } else if (x == width) {
                    dir = InteractableTile.InteractionDirection.Right;
                }

                InteractWithTile(tileLocation, dir);
            }
        }
        if (pipes) {
            for (int x = -width; x <= width; x++) {
                for (int y = maxY; y >= minY; y--) {
                    Vector3Int tileLocation = Utils.WorldToTilemapPosition(body.position + offset + new Vector2(x/2f, y/2f - 0.45f));
                    BreakablePipeTile pipe = GameManager.Instance.tilemap.GetTile<BreakablePipeTile>(tileLocation);
                    if (!pipe || !pipe.upsideDownPipe) continue;

                    InteractableTile.InteractionDirection dir = InteractableTile.InteractionDirection.Up;
                    if (y == minY && singlejump && onGround) {
                        dir = InteractableTile.InteractionDirection.Down;
                    } else if (x == -width) {
                        dir = InteractableTile.InteractionDirection.Left;
                    } else if (x == width) {
                        dir = InteractableTile.InteractionDirection.Right;
                    }

                    InteractWithTile(tileLocation, dir);
                }
            }
        }
    }

    int InteractWithTile(Vector3Int tilePos, InteractableTile.InteractionDirection direction) {
        if (!photonView.IsMine) return 0;

        TileBase tile = GameManager.Instance.tilemap.GetTile(tilePos);
        if (tile == null) return -1;
        if (tile is InteractableTile it) {
            return it.Interact(this, direction, Utils.TilemapToWorldPosition(tilePos)) ? 1 : 0;
        }
        return 0;
    }
    
    public void PlaySoundFromAnim(string sound, float volume = 1) {
        sfx.PlayOneShot((AudioClip) Resources.Load("Sound/" + sound), volume);
    }

    [PunRPC]
    protected void Knockback(bool fromRight, int starsToDrop, int attackerView) {
        if (invincible > 0 || knockback || hitInvincibilityCounter > 0) return;
        knockback = true;
        PhotonView attacker = PhotonNetwork.GetPhotonView(attackerView);
        if (attacker) {
            if (attacker.gameObject.CompareTag("Player") || attacker.gameObject.CompareTag("Fireball")) {
                //attacker is a player
                SpawnParticle("Prefabs/Particle/PlayerBounce", attacker.transform.position.x, attacker.transform.position.y);
            }
        }
        if ((fromRight && Physics2D.Raycast(body.position + new Vector2(0, 0.2f), Vector2.left, 0.3f, ONLY_GROUND_MASK)) ||
            (!fromRight && Physics2D.Raycast(body.position + new Vector2(0, 0.2f), Vector2.right, 0.3f, ONLY_GROUND_MASK))) {
            
            fromRight = !fromRight;
        }
        body.velocity = new Vector2((fromRight ? -1 : 1) * 3 * (starsToDrop + 1) * (state == Enums.PowerupState.Giant ? 3 : 1), 0);
        inShell = false;
        facingRight = !fromRight;
        groundpound = false;
        flying = false;
        drill = false;
        body.gravityScale = normalGravity;
        while (starsToDrop-- > 0) {
            SpawnStar();
        }
    }

    [PunRPC]
    protected void ResetKnockback() {
        hitInvincibilityCounter = 2f;
        bounce = false;
        knockback = false;
    }

    protected void Update() {
        HandleAnimations();

#if UNITY_EDITOR
        if (!photonView.IsMine) return;
        Keyboard kb = Keyboard.current;
        if (kb[Key.LeftBracket].wasPressedThisFrame) {
            Time.timeScale /= 2;
            Debug.Log("new timescale = " + Time.timeScale);
        }
        if (kb[Key.RightBracket].wasPressedThisFrame) {
            Time.timeScale *= 2;
            Debug.Log("new timescale = " + Time.timeScale);
        }
        DebugItem(Key.Numpad0, null);
        DebugItem(Key.Numpad1, "Mushroom");
        DebugItem(Key.Numpad2, "FireFlower");
        DebugItem(Key.Numpad3, "BlueShell");
        DebugItem(Key.Numpad4, "MiniMushroom");
        DebugItem(Key.Numpad5, "MegaMushroom");
        DebugItem(Key.Numpad6, "Star");
        DebugEntity(Key.Digit1, "Koopa");
        DebugEntity(Key.Digit2, "RedKoopa");
        DebugEntity(Key.Digit3, "BlueKoopa");
        DebugEntity(Key.Digit4, "Goomba");
        DebugEntity(Key.Digit5, "Bobomb");
        DebugEntity(Key.Digit6, "BulletBill");
#endif
    }

#if UNITY_EDITOR
    private void DebugItem(Key key, string item) {
        if (Keyboard.current[key].wasPressedThisFrame) {
            SpawnItem(item);
        }
    }
    private void DebugEntity(Key key, string entity) {
        if (Keyboard.current[key].wasPressedThisFrame) {
            PhotonNetwork.Instantiate("Prefabs/Enemy/" + entity, body.position + (facingRight ? Vector2.right : Vector2.left), Quaternion.identity);
        }
    }
#endif

    protected void FixedUpdate() {
        //game ended, freeze.
        
        if (GameManager.Instance) {
            if (!GameManager.Instance.musicEnabled) {
                models.SetActive(false);
                return;
            }
            if (GameManager.Instance.gameover) {
                body.velocity = Vector2.zero;
                animator.enabled = false;
                body.isKinematic = true;
                return;
            }
        }

        if (!dead) {
            HandleTemporaryInvincibility();
            bool snapped = GroundSnapCheck();
            HandleGroundCollision();
            onGround |= snapped;
            doGroundSnap = onGround || sliding;
            HandleCustomTiles();
            TickCounters();
            HandleMovement(Time.fixedDeltaTime);
            //snapped = GroundSnapCheck();
            //onGround |= snapped;
            //doGroundSnap = onGround || sliding;
        }
        // TickCounter(ref fadeOutTimer, 0, Time.fixedDeltaTime);
        // TickCounter(ref fadeInTimer, 0, Time.fixedDeltaTime);
        UpdateAnimatorStates();
    }

    void HandleSliding(bool up) {
        if (groundpound) {
            if (onGround) {
                if (state == Enums.PowerupState.Giant) {
                    groundpound = false;
                    groundpoundCounter = 0.5f;
                    return;
                } else {

                    if (!inShell && Mathf.Abs(floorAngle) >= 20) {
                        groundpound = false;
                        sliding = true;
                        alreadyGroundpounded = true;
                        body.velocity = new Vector2(-Mathf.Sign(floorAngle) * groundpoundVelocity, 0);
                    } else if (up || !crouching) {
                        groundpound = false;
                        groundpoundCounter = (state == Enums.PowerupState.Giant ? 0.4f : 0.25f);
                    }
                }
            }
            if (up && state != Enums.PowerupState.Giant) {
                groundpound = false;
            }
        }
        if (crouching && Mathf.Abs(floorAngle) >= 20 && !inShell && state != Enums.PowerupState.Giant) {
            sliding = true;
            crouching = false;
            alreadyGroundpounded = true;
        }
        if (sliding && onGround && Mathf.Abs(floorAngle) > 20) {
            float angleDeg = floorAngle * Mathf.Deg2Rad;

            bool uphill = Mathf.Sign(floorAngle) == Mathf.Sign(body.velocity.x);
            float speed = runningMaxSpeed * Time.fixedDeltaTime * 4f * (uphill && Mathf.Abs(body.velocity.x) > 1  ? 0.7f : 1f);

            float newX = Mathf.Clamp(body.velocity.x - (Mathf.Sin(angleDeg) * speed), -(runningMaxSpeed * 1.3f), (runningMaxSpeed * 1.3f));
            float newY = Mathf.Sin(angleDeg) * newX;
            body.velocity = new Vector2(newX, newY);
        }
            
        if (up || (Mathf.Abs(floorAngle) < 20 && onGround && Mathf.Abs(body.velocity.x) < 0.1)) {
            sliding = false;
            alreadyGroundpounded = false;
        }
    }

    void HandleSlopes() {
        if (!onGround) {
            floorAngle = 0;
            return;
        }
        BoxCollider2D mainCollider = hitboxes[0];
        RaycastHit2D hit = Physics2D.BoxCast(body.position + (Vector2.up * 0.05f), new Vector2(mainCollider.size.x * transform.lossyScale.x, 0.1f), 0, body.velocity.normalized, (body.velocity * Time.fixedDeltaTime).magnitude, ANY_GROUND_MASK);
        if (hit) {
            //hit ground
            float angle = Vector2.SignedAngle(Vector2.up, hit.normal); 
            if (angle < -89 || angle > 89) return;
            floorAngle = angle;

            float change = Mathf.Sin(angle * Mathf.Deg2Rad) * body.velocity.x * 1.25f;
            body.velocity = new Vector2(body.velocity.x, change);
            onGround = true;
            doGroundSnap = true;
        } else {
            hit = Physics2D.BoxCast(body.position + (Vector2.up * 0.05f), new Vector2(mainCollider.size.x - 0.1f, 0.1f) * transform.lossyScale, 0, Vector2.down, 0.3f, ANY_GROUND_MASK);
            if (hit) {
                float angle = Vector2.SignedAngle(Vector2.up, hit.normal); 
                if (angle < -89 || angle > 89) return;
                floorAngle = angle;

                float change = Mathf.Sin(angle * Mathf.Deg2Rad) * body.velocity.x * 1.25f;
                body.velocity = new Vector2(body.velocity.x, change);
                onGround = true;
                doGroundSnap = true;
            } else {
                floorAngle = 0;
            }
        }
        if (joystick.x == 0 && !inShell && !sliding && Mathf.Abs(floorAngle) > 40 && state != Enums.PowerupState.Giant) {
            //steep slope, continously walk downwards
            float autowalkMaxSpeed = floorAngle / 30;
            if (Mathf.Abs(body.velocity.x) > autowalkMaxSpeed) return;
            float newX = Mathf.Clamp(body.velocity.x - (autowalkMaxSpeed * Time.fixedDeltaTime), -Mathf.Abs(autowalkMaxSpeed), Mathf.Abs(autowalkMaxSpeed));
            body.velocity = new Vector2(newX, Mathf.Sin(floorAngle * Mathf.Deg2Rad) * newX);
        }
    }

    bool colliding = true;
    void HandleTemporaryInvincibility() {
        bool shouldntCollide = (hitInvincibilityCounter > 0) || knockback;
        if (shouldntCollide && colliding) {
            colliding = false;
            foreach (var player in GameManager.Instance.allPlayers) {
                foreach (BoxCollider2D hitbox in hitboxes) {
                    foreach (BoxCollider2D otherHitbox in player.hitboxes) {
                        Physics2D.IgnoreCollision(hitbox, otherHitbox, true);
                    }
                }
            }
        } else if (!shouldntCollide && !colliding) {
            colliding = true;
            foreach (var player in GameManager.Instance.allPlayers) {
                foreach (BoxCollider2D hitbox in hitboxes) {
                    foreach (BoxCollider2D otherHitbox in player.hitboxes) {
                        Physics2D.IgnoreCollision(hitbox, otherHitbox, false);
                    }
                }
            }
        }
    }

    void HandleCustomTiles() {
        doIceSkidding = false;
        tileFriction = -1;
        footstepMaterial = "";
        foreach (Vector3Int pos in tilesStandingOn) {
            TileBase tile = Utils.GetTileAtTileLocation(pos);
            if (tile == null) continue;
            if (tile is TileWithProperties propTile) {
                footstepMaterial = propTile.footstepMaterial;
                doIceSkidding = propTile.iceSkidding;
                tileFriction = Mathf.Max(tileFriction, propTile.frictionFactor);
            } else {
                tileFriction = 1;
            }
        }
        if (tileFriction == -1) {
            tileFriction = 1;
        }
    }

    void HandleDeathAnimation() {
        if (!dead) return;
        if (body.position.y < GameManager.Instance.GetLevelMinY() - transform.lossyScale.y) {
            transform.position = body.position = new Vector2(body.position.x, GameManager.Instance.GetLevelMinY() - 20);
        }

        deathCounter += Time.fixedDeltaTime;
        if (deathCounter < deathUpTime) {
            deathUp = false;
            body.gravityScale = 0;
            body.velocity = Vector2.zero;
        } else {
            if (!deathUp && body.position.y > GameManager.Instance.GetLevelMinY()) {
                body.velocity = new Vector2(0, deathForce);
                deathUp = true;
            }
            body.gravityScale = 1.2f;
            body.velocity = new Vector2(0, Mathf.Max(-deathForce, body.velocity.y));
        }

        if (photonView.IsMine && deathCounter >= 3f) {
            photonView.RPC("PreRespawn", RpcTarget.All);
        }
    }
    
    void HandleAnimations() {
        Vector3 targetEuler = models.transform.eulerAngles;
        bool instant = false;
        if (dead || animator.GetBool("pipe")) {
            targetEuler = new Vector3(0, 180, 0);
            instant = true;
        } else if (animator.GetBool("inShell") && !onSpinner) {
            targetEuler += (Mathf.Abs(body.velocity.x) / runningMaxSpeed) * Time.deltaTime * new Vector3(0, 1800 * (facingRight ? -1 : 1));
            instant = true;
        } else if (skidding || turnaround) {
            if (facingRight ^ (animator.GetCurrentAnimatorStateInfo(0).IsName("turnaround") || turnaround)) {
                targetEuler = new Vector3(0, 360 - 100, 0);
            } else {
                targetEuler = new Vector3(0, 100, 0);
            }
        } else {
            if (onSpinner && onGround && Mathf.Abs(body.velocity.x) < 0.3f && !holding) {
                targetEuler += new Vector3(0, -1800, 0) * Time.deltaTime;
                instant = true;
            } else if (flying) {
                if (drill) {
                    targetEuler += new Vector3(0, -2000, 0) * Time.deltaTime;
                } else {
                    targetEuler += new Vector3(0, -1200, 0) * Time.deltaTime;
                }
                instant = true;
            } else {
                if (facingRight) {
                    targetEuler = new Vector3(0, 100, 0);
                } else {
                    targetEuler = new Vector3(0, 360 - 100, 0);
                }
            }
        }
        if (instant || wasTurnaround) {
            models.transform.rotation = Quaternion.Euler(targetEuler);
        } else {
            float maxRotation = 2000f * Time.deltaTime;
            float x = models.transform.eulerAngles.x, y = models.transform.eulerAngles.y, z = models.transform.eulerAngles.z;
            x += Mathf.Max(Mathf.Min(maxRotation, targetEuler.x - x), -maxRotation);
            y += Mathf.Max(Mathf.Min(maxRotation, targetEuler.y - y), -maxRotation);
            z += Mathf.Max(Mathf.Min(maxRotation, targetEuler.z - z), -maxRotation);
            models.transform.rotation = Quaternion.Euler(x, y, z);
        }
        wasTurnaround = animator.GetCurrentAnimatorStateInfo(0).IsName("turnaround") || turnaround;

        //Particles
        SetParticleEmission(dust, (onLeft || onRight || (onGround && ((skidding && !doIceSkidding) || (crouching && Mathf.Abs(body.velocity.x) > 1))) || (sliding && Mathf.Abs(body.velocity.x) > 0.2 && onGround)) && !pipeEntering);
        SetParticleEmission(drillParticle, drill);
        SetParticleEmission(sparkles, invincible > 0);
        SetParticleEmission(giantParticle, state == Enums.PowerupState.Giant && giantStartTimer < 0);

        //Blinking
        if (dead) {
            eyeState = Enums.PlayerEyeState.Death;
        } else {
            if ((blinkTimer -= Time.fixedDeltaTime) < 0) {
                blinkTimer = 3f + (Random.value * 2f);
            }
            if (blinkTimer < blinkDuration) {
                eyeState = Enums.PlayerEyeState.HalfBlink;
            } else if (blinkTimer < blinkDuration * 2f) {
                eyeState = Enums.PlayerEyeState.FullBlink;
            } else if (blinkTimer < blinkDuration * 3f) {
                eyeState = Enums.PlayerEyeState.HalfBlink;
            } else {
                eyeState = Enums.PlayerEyeState.Normal;
            }
        }

        HorizontalCamera.OFFSET_TARGET = (flying ? 0.75f : 0f);
        if (flying) {
            float percentage = Mathf.Abs(body.velocity.x) / walkingMaxSpeed;
            cameraController.offset = 2f * percentage * (body.velocity.x > 0 ? cameraOffsetRight : cameraOffsetLeft);
            cameraController.exactCentering = true;
        } else {
            cameraController.offset = cameraOffsetZero;
            cameraController.exactCentering = false;
        }

        if (crouching || sliding || skidding) {
            onLeft = false;
            onRight = false;
            dust.transform.localPosition = Vector2.zero;
        }
    }

    void UpdateAnimatorStates() {

        if (photonView.IsMine) {

            //Facing direction
            bool right = joystick.x > analogDeadzone;
            bool left = joystick.x < -analogDeadzone;
            if (!sliding) {
                if (doIceSkidding && !inShell && !groundpound) {
                    if (right || left) {
                        facingRight = right;
                    }
                } else if (giantStartTimer <= 0 && giantEndTimer <= 0 && !skidding && !(animator.GetCurrentAnimatorStateInfo(0).IsName("turnaround") || turnaround)) {
                    if (onGround && state != Enums.PowerupState.Giant && Mathf.Abs(body.velocity.x) > 0.05f) {
                        facingRight = body.velocity.x > 0;
                    } else if (((walljumping <= 0 && !inShell) || giantStartTimer > 0) && (right || left)) {
                        facingRight = right;
                    }
                    if (!inShell && ((Mathf.Abs(body.velocity.x) < 0.5f && crouching) || doIceSkidding) && (right || left)) {
                        facingRight = right;
                    }
                }
            }

            //Animation
            animator.SetBool("skidding", !doIceSkidding && skidding);
            animator.SetBool("turnaround", turnaround);
            animator.SetBool("onLeft", onLeft);
            animator.SetBool("onRight", onRight);
            animator.SetBool("onGround", onGround);
            animator.SetBool("invincible", invincible > 0);
            float animatedVelocity = Mathf.Abs(body.velocity.x) + Mathf.Abs(body.velocity.y * Mathf.Sin(floorAngle * Mathf.Deg2Rad));
            if (stuckInBlock) {
                animatedVelocity = 0;
            } else if (doIceSkidding) {
                if (skidding) {
                    animatedVelocity = 3.5f;
                }
                if (iceSliding) {
                    animatedVelocity = 0f;
                }
            }
            animator.SetFloat("velocityX", animatedVelocity);
            animator.SetFloat("velocityY", body.velocity.y);
            animator.SetBool("doublejump", doublejump);
            animator.SetBool("triplejump", triplejump);
            animator.SetBool("crouching", crouching);
            animator.SetBool("groundpound", groundpound);
            animator.SetBool("sliding", sliding);
            animator.SetBool("holding", holding != null);
            animator.SetBool("knockback", knockback);
            animator.SetBool("pipe", pipeEntering != null);
            animator.SetBool("mini", state == Enums.PowerupState.Mini);
            animator.SetBool("mega", state == Enums.PowerupState.Giant);
            animator.SetBool("flying", flying);
            animator.SetBool("drill", drill);
            animator.SetBool("inShell", inShell || (state == Enums.PowerupState.Shell && (crouching || groundpound)));
            animator.SetBool("facingRight", facingRight);
        } else {
            onLeft = animator.GetBool("onLeft");
            onRight = animator.GetBool("onRight");
            onGround = animator.GetBool("onGround");
            skidding = animator.GetBool("skidding");
            turnaround = animator.GetBool("turnaround");
            crouching = animator.GetBool("crouching");
            invincible = animator.GetBool("invincible") ? 1f : 0f;
            flying = animator.GetBool("flying");
            drill = animator.GetBool("drill");
            sliding = animator.GetBool("sliding");
            // inShell = animator.GetBool("inShell");
            // knockback = animator.GetBool("knockback");
            facingRight = animator.GetBool("facingRight");
        }
        
        if (giantEndTimer > 0) {
            transform.localScale = Vector3.one + (Vector3.one * (Mathf.Min(1, giantEndTimer / (giantStartTime / 2f)) * 2.6f));
        } else {
            transform.localScale = state switch {
                Enums.PowerupState.Mini => Vector3.one / 2,
                Enums.PowerupState.Giant => Vector3.one + (Vector3.one * (Mathf.Min(1, 1 - (giantStartTimer / giantStartTime)) * 2.6f)),
                _ => Vector3.one,
            };
        }
    
        //Enable rainbow effect
        MaterialPropertyBlock block = new(); 
        block.SetColor("GlowColor", glowColor);
        block.SetFloat("RainbowEnabled", (animator.GetBool("invincible") ? 1.1f : 0f));
        block.SetFloat("FireEnabled", (state == Enums.PowerupState.FireFlower ? 1.1f : 0f));
        block.SetFloat("EyeState", (int) eyeState);
        block.SetFloat("ModelScale", transform.lossyScale.x);
        Vector3 giantMultiply = Vector3.one;
        if (giantTimer > 0 && giantTimer < 4) {
            float v = (((Mathf.Sin(giantTimer * 20f) + 1f) / 2f) * 0.9f) + 0.1f;
            giantMultiply = new Vector3(v, 1, v);
        }
        block.SetVector("MultiplyColor", giantMultiply);
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>()) {
            renderer.SetPropertyBlock(block);
        }
        foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>()) {
            renderer.SetPropertyBlock(block);
        }
        
        //Hitbox changing
        UpdateHitbox();

        //hit flash
        if (hitInvincibilityCounter >= 0) {
            hitInvincibilityCounter -= Time.fixedDeltaTime;
            
            bool invisible;
            if (hitInvincibilityCounter <= 0.75f) {
                invisible = ((hitInvincibilityCounter * 5f) % (blinkingSpeed*2f) < blinkingSpeed);
            } else {
                invisible = (hitInvincibilityCounter * 2f) % (blinkingSpeed*2) < blinkingSpeed;
            }
            models.SetActive(!invisible);
        } else {
            models.SetActive(true);
        }

        //Model changing
        bool large = state >= Enums.PowerupState.Large;

        largeModel.SetActive(large);
        smallModel.SetActive(!large);
        blueShell.SetActive(state == Enums.PowerupState.Shell);
        animator.avatar = large ? largeAvatar : smallAvatar;

        HandleDeathAnimation();
        HandlePipeAnimation();

        if (animator.GetBool("pipe")) {
            gameObject.layer = HITS_NOTHING_LAYERID;
            transform.position = new Vector3(body.position.x, body.position.y, 1);
        } else if (dead || stuckInBlock || giantStartTimer > 0 || giantEndTimer > 0) {
            gameObject.layer = HITS_NOTHING_LAYERID;
            transform.position = new Vector3(body.position.x, body.position.y, -4);
        } else {
            gameObject.layer = DEFAULT_LAYERID;
            transform.position = new Vector3(body.position.x, body.position.y, -4);
        }
    }

    private void SetParticleEmission(ParticleSystem particle, bool value) {
        if (value) {
            if (particle.isStopped) {
                particle.Play();
            }
        } else {
            if (particle.isPlaying) {
                particle.Stop();
            }
        }
    }

    [PunRPC]
    public void SetHolding(int view) {
        holding = PhotonView.Find(view).GetComponent<HoldableEntity>();
    }
    [PunRPC]
    public void SetHoldingOld(int view) {
        holdingOld = PhotonView.Find(view).GetComponent<HoldableEntity>();
        throwInvincibility = 0.5f;
    }

    void UpdateHitbox() {
        float width = hitboxes[0].size.x;
        float height;

        if (state <= Enums.PowerupState.Small || (invincible > 0 && !onGround && !crouching && !sliding) || groundpound) {
            height = heightSmallModel;
        } else {
            height = heightLargeModel;
        }

        if (crouching || inShell || sliding) {
            height *= (state <= Enums.PowerupState.Small ? 0.7f : 0.5f);
        }

        hitboxes[0].size = new Vector2(width, height);
        hitboxes[0].offset = new Vector2(0, height/2f);
    }

    bool GroundSnapCheck() {
        if ((body.velocity.y > 0 && !onGround)|| !doGroundSnap || pipeEntering) return false;
       
        BoxCollider2D hitbox = hitboxes[0];
        RaycastHit2D hit = Physics2D.BoxCast(body.position + new Vector2(0, 0.1f), new Vector2(hitbox.size.x * transform.lossyScale.x, 0.05f), 0, Vector2.down, 0.4f, ANY_GROUND_MASK);
        if (hit) {
            body.position = new Vector2(body.position.x, hit.point.y + Physics2D.defaultContactOffset);
            Debug.Log("Snap");
            return true;
        }
        return false;
    }
    
    void HandlePipeAnimation() {
        if (!photonView.IsMine) return;
        if (!pipeEntering) {
            pipeTimer = 0;
            return;
        }

        body.isKinematic = true;
        body.velocity = pipeDirection;
            
        if (pipeTimer < pipeDuration / 2f && pipeTimer+Time.fixedDeltaTime >= pipeDuration / 2f) {
            //tp to other pipe
            if (pipeEntering.otherPipe.bottom == pipeEntering.bottom) {
                pipeDirection *= -1;
            }
            Vector2 offset = (pipeDirection * (pipeDuration / 2f));
            if (pipeEntering.otherPipe.bottom) {
                offset -= pipeDirection;
                offset.y -= heightLargeModel - (hitboxes[0].size.y * transform.localScale.y);
            }
            transform.position = body.position = new Vector3(pipeEntering.otherPipe.transform.position.x, pipeEntering.otherPipe.transform.position.y, 1) - (Vector3) offset;
            photonView.RPC("PlaySound", RpcTarget.All, "player/pipe");
        }
        if (pipeTimer >= pipeDuration) {
            pipeEntering = null;
            body.isKinematic = false;
        }
        pipeTimer += Time.fixedDeltaTime;
    }

    void DownwardsPipeCheck() {
        if (!photonView.IsMine) return;
        if (state == Enums.PowerupState.Giant) return;
        if (!onGround || knockback || inShell) return;
        if (!(crouching || sliding)) return;

        foreach (RaycastHit2D hit in Physics2D.RaycastAll(body.position, Vector2.down, 1f)) {
            GameObject obj = hit.transform.gameObject;
            if (!obj.CompareTag("pipe")) continue;
            PipeManager pipe = obj.GetComponent<PipeManager>();
            if (pipe.miniOnly && state != Enums.PowerupState.Mini) continue;
            
            //Enter pipe
            pipeEntering = pipe;
            pipeDirection = Vector2.down;

            body.velocity = Vector2.down;
            transform.position = body.position = new Vector2(obj.transform.position.x, transform.position.y);

            photonView.RPC("PlaySound", RpcTarget.All, "player/pipe");
            crouching = false;
            sliding = false;
            break;
        }
    }

    void UpwardsPipeCheck() {
        if (!photonView.IsMine) return;
        bool uncrouch = joystick.y > analogDeadzone;
        if (!hitRoof) return;
        if (!uncrouch) return;
        if (state == Enums.PowerupState.Giant) return;

        //todo: change to nonalloc?
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(body.position, Vector2.up, 1f)) {
            GameObject obj = hit.transform.gameObject;
            if (!obj.CompareTag("pipe")) continue;
            PipeManager pipe = obj.GetComponent<PipeManager>();
            if (pipe.miniOnly && state != Enums.PowerupState.Mini) continue;

            //pipe found
            pipeEntering = pipe;
            pipeDirection = Vector2.up;

            body.velocity = Vector2.up;
            transform.position = body.position = new Vector2(obj.transform.position.x, transform.position.y);
                
            photonView.RPC("PlaySound", RpcTarget.All, "player/pipe");
            crouching = false;
            sliding = false;
            break;
        }
    }
    
    void HandleCrouching(bool crouchInput) {
        if (sliding) return;
        if (state == Enums.PowerupState.Giant) {
            crouching = false;
            return;
        }
        bool prevCrouchState = crouching;
        crouching = ((onGround && crouchInput) || (!onGround && crouchInput && crouching) || (crouching && ForceCrouchCheck())) && !holding;
        if (crouching && !prevCrouchState) {
            //crouch start sound
            PlaySoundFromAnim("player/crouch");
        }
    }

    bool ForceCrouchCheck() {
        if (state < Enums.PowerupState.Large) return false;
        float width = hitboxes[0].bounds.extents.x;

        bool triggerState = Physics2D.queriesHitTriggers;
        Physics2D.queriesHitTriggers = false;
        
        bool ret = Physics2D.BoxCast(body.position + new Vector2(0, heightLargeModel / 2f + 0.1f), new Vector2(width, heightLargeModel-0.1f), 0, Vector2.zero, 0, ONLY_GROUND_MASK);
        
        Physics2D.queriesHitTriggers = triggerState;
        return ret;
    }

    void HandleWallslide(bool leftWall, bool jump, bool holdingDirection) {
        triplejump = false;
        doublejump = false;
        singlejump = false;

        body.velocity = new Vector2(0, Mathf.Max(body.velocity.y, wallslideSpeed));
        dust.transform.localPosition = new Vector2(hitboxes[0].size.x * (3f/4f) * (leftWall ? -1 : 1), hitboxes[0].size.y * (3f/4f));
            
        if (jump) {
            float offsetX = hitboxes[0].size.x/2f * (leftWall ? -1 : 1);
            float offsetY = hitboxes[0].size.y/2f;
            photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/WalljumpParticle", body.position.x + offsetX, body.position.y + offsetY, leftWall ? Vector3.zero : Vector3.up * 180);
        
            onLeft = false;
            onRight = false;
            body.velocity = new Vector2(runningMaxSpeed * (3/4f) * (leftWall ? 1 : -1), walljumpVelocity);
            walljumping = 0.5f;
            facingRight = leftWall;
            singlejump = true;
            doublejump = false;
            triplejump = false;
            onGround = false;
            photonView.RPC("PlaySound", RpcTarget.All, "player/walljump");
            if (Random.value < 0.5) {
                photonView.RPC("PlaySound", RpcTarget.All, character.soundFolder + "/walljump_1");
            } else {
                photonView.RPC("PlaySound", RpcTarget.All, character.soundFolder + "/walljump_2");
            }
        }

        if (!holdingDirection) {
            body.position += new Vector2(0.05f * (leftWall ? 1 : -1), 0);
            onLeft = false;
            onRight = false;
        }
    }
    
    void HandleJumping(bool jump) {
        if (knockback || drill) return;
        if (groundpound || groundpoundCounter > 0) return;
        if (state == Enums.PowerupState.Giant && singlejump) return;

        bool topSpeed = Mathf.Abs(body.velocity.x) + 0.1f > (runningMaxSpeed * (invincible > 0 ? 2 : 1));
        if (bounce || (jump && (onGround || koyoteTime < 0.2f))) {
            koyoteTime = 1;
            jumpBuffer = 0;
            skidding = false;
            turnaround = false;
            sliding = false;
            alreadyGroundpounded = false;

            if (onSpinner && !inShell && !holding && !(crouching && state == Enums.PowerupState.Shell)) {
                photonView.RPC("PlaySound", RpcTarget.All, character.soundFolder + "/spinner_launch");
                photonView.RPC("PlaySound", RpcTarget.All, "player/spinner_launch");
                body.velocity = new Vector2(body.velocity.x, launchVelocity);
                flying = true;
                onGround = false;
                return;
            }

            float vel = jumpVelocity + Mathf.Abs(body.velocity.x)/8f * (state == Enums.PowerupState.Giant ? 1.5f : 1f);
            if (!flying && topSpeed && landing < 0.1f && !holding && !triplejump && !crouching && !inShell && invincible <= 0 && ((body.velocity.x < 0 && !facingRight) || (body.velocity.x > 0 && facingRight))) {
                bool canSpecialJump = !Physics2D.Raycast(body.position + new Vector2(0, 0.1f), Vector2.up, 1f, ONLY_GROUND_MASK);
                if (singlejump && canSpecialJump) {
                    //Double jump
                    photonView.RPC("PlaySound", RpcTarget.All, character.soundFolder + "/double_jump_" +  ((int) (Random.value * 2f) + 1));
                    singlejump = false;
                    doublejump = true;
                    triplejump = false;
                    body.velocity = new Vector2(body.velocity.x, vel);
                } else if (doublejump && canSpecialJump) {
                    //Triple jump
                    photonView.RPC("PlaySound", RpcTarget.All, character.soundFolder + "/triple_jump");
                    singlejump = false;
                    doublejump = false;
                    triplejump = true;
                    body.velocity = new Vector2(body.velocity.x, vel + 0.5f);
                } else {
                    //Normal jump
                    singlejump = true;
                    doublejump = false;
                    triplejump = false;
                    body.velocity = new Vector2(body.velocity.x, vel);
                }
            } else {
                //Normal jump
                singlejump = true;
                doublejump = false;
                triplejump = false;
                body.velocity = new Vector2(body.velocity.x, vel);
                if (!bounce) {
                    drill = false;
                    flying = false;
                }
            }
            if (!bounce) {
                //play jump
                string sound = "jump";
                switch (state) {
                case Enums.PowerupState.Giant: {
                    sound = "jump_mega";
                    break;
                }
                case Enums.PowerupState.Mini: {
                    sound = "jump_mini";
                    break;
                }
                }
                photonView.RPC("PlaySound", RpcTarget.All, "player/" + sound);
            }
            bounce = false;
            onGround = false;
            body.position += Vector2.up * 0.075f;
        }
    }

    void HandleWalkingRunning(bool left, bool right) {
        if (groundpound || sliding || knockback || pipeEntering) return;
        if (groundpoundCounter > 0) return;
        if (!(walljumping <= 0 || onGround)) return;

        iceSliding = false;
        if (!left && !right) {
            skidding = false;
            turnaround = false;
            if (doIceSkidding) {
                iceSliding = true;
            }
        }

        if (Mathf.Abs(body.velocity.x) < 0.5f || !onGround) {
            skidding = false;
        }

        if (inShell) {
            body.velocity = new Vector2(runningMaxSpeed * (facingRight ? 1 : -1), body.velocity.y);
            return;
        }

        if ((left && right) || !(left || right)) return;

        float invincibleSpeedBoost = (invincible > 0 ? 2f : 1);
        float airPenalty = (onGround ? 1 : 0.5f);
        float xVel = body.velocity.x;
        float runSpeedTotal = runningMaxSpeed * invincibleSpeedBoost;
        float walkSpeedTotal = walkingMaxSpeed * invincibleSpeedBoost;
        bool reverseSlowing = onGround && (((left && body.velocity.x > 0.02) || (right && body.velocity.x < -0.02)));
        float reverseFloat = (reverseSlowing && doIceSkidding ? 0.4f : 1);
        float turnaroundSpeedBoost = (turnaround && !reverseSlowing ? 5 : 1);
        float stationarySpeedBoost = Mathf.Abs(body.velocity.x) <= 0.005f ? 1f : 1f;

        if ((crouching && !onGround && state != Enums.PowerupState.Shell) || !crouching) {
            
            if (left) {
                if (functionallyRunning && !flying && xVel <= -(walkingMaxSpeed - 0.3f)) {
                    skidding = false;
                    turnaround = false;
                    if (xVel > -runSpeedTotal) {
                        float change = invincibleSpeedBoost * invincibleSpeedBoost * invincibleSpeedBoost * turnaroundSpeedBoost * runningAcceleration * airPenalty * stationarySpeedBoost * Time.fixedDeltaTime;    
                        body.velocity += new Vector2(change * -1, 0);
                    }
                } else {
                    if (xVel > -walkSpeedTotal) {
                        float change = invincibleSpeedBoost * reverseFloat * turnaroundSpeedBoost * walkingAcceleration * stationarySpeedBoost * Time.fixedDeltaTime;
                        body.velocity += new Vector2(change * -1, 0);
                        
                        if (state != Enums.PowerupState.Giant && reverseSlowing && xVel > runSpeedTotal - 2) {
                            skidding = true;
                            turnaround = true;
                            facingRight = true;
                        }
                    }
                }
            }
            if (right) {
                if (functionallyRunning && !flying && xVel >= (walkingMaxSpeed - 0.3f)) {
                    skidding = false;
                    turnaround = false;
                    if (xVel < runSpeedTotal) {
                        float change = invincibleSpeedBoost * invincibleSpeedBoost * invincibleSpeedBoost * turnaroundSpeedBoost * runningAcceleration * airPenalty * stationarySpeedBoost * Time.fixedDeltaTime;
                        body.velocity += new Vector2(change * 1, 0);
                    }
                } else {
                    if (xVel < walkSpeedTotal) {
                        float change = invincibleSpeedBoost * reverseFloat * turnaroundSpeedBoost * walkingAcceleration * stationarySpeedBoost * Time.fixedDeltaTime;
                        body.velocity += new Vector2(change * 1, 0);

                        if (state != Enums.PowerupState.Giant && reverseSlowing && xVel < -runSpeedTotal + 2) {
                            skidding = true;
                            turnaround = true;
                            facingRight = false;
                        }
                    }
                }
            }
        } else {
            turnaround = false;
            skidding = false;
        }

        if (state == Enums.PowerupState.Shell && !inShell && onGround && functionallyRunning && !holding && Mathf.Abs(xVel)+0.25f >= runningMaxSpeed && landing > 0.33f) {
            inShell = true;
        }
        if (onGround) {
            body.velocity = new Vector2(body.velocity.x, 0);
        }
    }

    bool HandleStuckInBlock() {
        if (!body || hitboxes == null) return false;
        Vector2 checkPos = body.position + new Vector2(0, hitboxes[0].size.y/4f);
        if (!Utils.IsTileSolidAtWorldLocation(checkPos)) {
            stuckInBlock = false;
            return false;
        }
        stuckInBlock = true;
        body.gravityScale = 0;
        onGround = true;
        if (!Utils.IsTileSolidAtWorldLocation(checkPos + new Vector2(0, 0.3f))) {
            transform.position = body.position = new Vector2(body.position.x, Mathf.Floor((checkPos.y + 0.3f) * 2) / 2);
            return true;
        } else if (!Utils.IsTileSolidAtWorldLocation(checkPos - new Vector2(0, 0.3f))) {
            transform.position = body.position = new Vector2(body.position.x, Mathf.Floor((checkPos.y - 0.3f) * 2) / 2);
            return true;
        } else if (!Utils.IsTileSolidAtWorldLocation(checkPos + new Vector2(0.25f, 0))) {
            body.velocity = Vector2.right * 2f;
            return true;
        } else if (!Utils.IsTileSolidAtWorldLocation(checkPos + new Vector2(-0.25f, 0))) {
            body.velocity = Vector2.left * 2f;
            return true;
        }
        RaycastHit2D rightRaycast = Physics2D.Raycast(checkPos, Vector2.right, 15, ONLY_GROUND_MASK);
        RaycastHit2D leftRaycast = Physics2D.Raycast(checkPos, Vector2.left, 15, ONLY_GROUND_MASK);
        float rightDistance = 0, leftDistance = 0;
        if (rightRaycast) rightDistance = rightRaycast.distance;
        if (leftRaycast) leftDistance = leftRaycast.distance;
        if (rightDistance <= leftDistance) {
            body.velocity = Vector2.right*2f;
        } else {
            body.velocity = Vector2.left*2f;
        }
        return true;
    }

    void TickCounter(ref float counter, float min, float delta) {
        counter = Mathf.Max(0, counter - delta); 
    }

    void TickCounters() {
        float delta = Time.fixedDeltaTime;
        if (!pipeEntering) TickCounter(ref invincible, 0, delta);

        TickCounter(ref throwInvincibility, 0, delta);
        TickCounter(ref jumpBuffer, 0, delta);
        TickCounter(ref walljumping, 0, delta);
        if (giantStartTimer <= 0) TickCounter(ref giantTimer, 0, delta);
        TickCounter(ref giantStartTimer, 0, delta);
        TickCounter(ref groundpoundCounter, 0, delta);
        TickCounter(ref giantEndTimer, 0, delta);
        TickCounter(ref groundpoundDelay, 0, delta);
    }

    [PunRPC]
    public void FinishMegaMario(bool success) {
        if (success) {
            PlaySound(character.soundFolder + "/mega_start");
        } else {
            //hit a wall, cancel
            savedVelocity = Vector2.zero;
            state = Enums.PowerupState.Large;
            giantEndTimer = giantStartTime;
            storedPowerup = "MegaMushroom";
            giantTimer = 0;
            animator.enabled = true;
            animator.Play("mega-cancel", 1);
            PlaySound("player/reserve_item_store");
        }
        body.isKinematic = false;
    }

    IEnumerator CheckForGiantStartupTiles() {
        HandleGiantTiles(false);
        yield return null;
        Vector2 o = body.position + new Vector2(0.3f * (facingRight ? 1 : -1), 1.75f);
        RaycastHit2D hit = Physics2D.BoxCast(o, new Vector2(0.6f, 3f), 0, Vector2.zero, 0, ONLY_GROUND_MASK);
        photonView.RPC("FinishMegaMario", RpcTarget.All, !(bool) hit);
    }

    void HandleMovement(float delta) {
        functionallyRunning = running || state == Enums.PowerupState.Giant;

        if (photonView.IsMine && body.position.y < GameManager.Instance.GetLevelMinY()) {
            //death via pit
            photonView.RPC("Death", RpcTarget.All, true);
            return;
        }

        bool paused = GameManager.Instance.paused;

        if (giantStartTimer > 0) {
            body.velocity = Vector2.zero;
            if (giantStartTimer - delta <= 0) {
                //start by checking bounding
                giantStartTimer = 0;
                if (photonView.IsMine)
                    StartCoroutine(CheckForGiantStartupTiles());
            } else {
                body.isKinematic = true;
                if (animator.GetCurrentAnimatorClipInfo(1).Length <= 0 || animator.GetCurrentAnimatorClipInfo(1)[0].clip.name != "mega-scale") {
                    animator.Play("mega-scale", 1);
                }
            }
            return;
        }
        if (giantEndTimer > 0) {
            body.velocity = Vector2.zero;
            body.isKinematic = true;

            if (giantEndTimer - delta <= 0) {
                hitInvincibilityCounter = 3f;
                body.velocity = savedVelocity;
                animator.enabled = true;
                body.isKinematic = false;
            }
            return;
        }

        if (state == Enums.PowerupState.Giant) {
            HandleGiantTiles(true);
            if (giantTimer <= 0) {
                // works??
                if (state != Enums.PowerupState.Large) {
                    savedVelocity = body.velocity;
                } else {
                    savedVelocity = Vector2.zero;
                }
                giantEndTimer = giantStartTime / 2f;
                state = Enums.PowerupState.Large;
                hitInvincibilityCounter = 3f;
                body.isKinematic = true;
                animator.enabled = false;
            }
        }
        
        //pipes > stuck in block, else the animation gets janked.
        if (pipeEntering) return;
        if (HandleStuckInBlock()) return;


        //Pipes
        if (!paused) {
            DownwardsPipeCheck();
            UpwardsPipeCheck();
        }
        
        if (knockback) {
            onLeft = false;
            onRight = false;
            crouching = false;
            inShell = false;
            body.velocity -= (body.velocity * (delta * 2f));
            if (photonView.IsMine && onGround && Mathf.Abs(body.velocity.x) < 0.2f) {
                photonView.RPC("ResetKnockback", RpcTarget.All);
            }
            if (holding) {
                holding.photonView.RPC("Throw", RpcTarget.All, !facingRight, true);
                holding = null;
            }
        }

        //activate blocks jumped into
        if (hitRoof) {
            body.velocity = new Vector2(body.velocity.x, Mathf.Min(body.velocity.y, -0.1f));
            foreach (Vector3Int tile in tilesJumpedInto) {
                InteractWithTile(tile, InteractableTile.InteractionDirection.Up);
            }
        }

        bool right = joystick.x > analogDeadzone && !paused;
        bool left = joystick.x < -analogDeadzone && !paused;
        bool crouch = joystick.y < -analogDeadzone && !paused;
        bool up = joystick.y > analogDeadzone && !paused;
        bool jump = (jumpBuffer > 0 && (onGround || koyoteTime < 0.1f || onLeft || onRight)) && !paused; 

        if (!crouch) {
            alreadyGroundpounded = false;
        }

        if (holding) {
            onLeft = false;
            onRight = false;
            holding.holderOffset = new Vector2((facingRight ? 1 : -1) * 0.25f, (state >= Enums.PowerupState.Large ? 0.5f : 0.25f));
        }
        
        //throwing held item
        if ((!functionallyRunning || state == Enums.PowerupState.Mini || state == Enums.PowerupState.Giant || invincible > 0) && holding) {
            bool throwLeft = !facingRight;
            if (left) {
                throwLeft = true;
            }
            if (right) {
                throwLeft = false;
            }
            holding.photonView.RPC("Throw", RpcTarget.All, throwLeft, crouch);
            if (!crouch && !knockback) {
                photonView.RPC("PlaySound", RpcTarget.All, character.soundFolder + "/walljump_2");
                throwInvincibility = 0.5f;
                animator.SetTrigger("throw");
            }
            holdingOld = holding;
            holding = null;
        }

        //blue shell enter/exit
        if (state != Enums.PowerupState.Shell || !functionallyRunning) {
            inShell = false;
        }
        if (inShell) {
            crouch = true;
            if (photonView.IsMine && (hitLeft || hitRight)) {
                foreach (var tile in tilesHitSide) {
                    InteractWithTile(tile, InteractableTile.InteractionDirection.Up);
                }
                facingRight = hitLeft;
                photonView.RPC("PlaySound", RpcTarget.All, "player/block_bump");
            }
        }

        //Ground
        if (onGround) {
            if (photonView.IsMine && hitRoof && crushGround && body.velocity.y <= 0.1) {
                //Crushed.
                photonView.RPC("Powerdown", RpcTarget.All, true);
            }
            koyoteTime = 0;
            onLeft = false;
            onRight = false;
            flying = false;
            if (triplejump && landing == 0 && !(left || right) && !groundpound) {
                if (!doIceSkidding)
                    body.velocity = new Vector2(0,0);
                animator.Play("jumplanding", state >= Enums.PowerupState.Large ? 1 : 0);
                photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/GroundpoundDust");
            }
            if (singlejump && state == Enums.PowerupState.Giant) {
                photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/GroundpoundDust");
                singlejump = false;
            }
            if ((landing += delta) > 0.1f) {
                singlejump = false;
                doublejump = false;
                triplejump = false;
            }
        
            if (onSpinner && Mathf.Abs(body.velocity.x) < 0.3f && !holding) {
                Transform spnr = onSpinner.transform;
                if (body.position.x > spnr.transform.position.x + 0.02f) {
                    body.position -= (new Vector2(0.01f * 60f, 0) * Time.fixedDeltaTime);
                } else if (body.position.x < spnr.transform.position.x - 0.02f) {
                    body.position += (new Vector2(0.01f * 60f, 0) * Time.fixedDeltaTime);
                }
            }
        } else {
            koyoteTime += delta;
            landing = 0;
            skidding = false;
            turnaround = false;
        }

        //Crouching
        HandleCrouching(crouch);

        if (onLeft) {
            HandleWallslide(true, jump, left);
        }
        if (onRight) {
            HandleWallslide(false, jump, right);
        }

        if ((walljumping <= 0 || onGround) && !groundpound) {
            //Normal walking/running
            HandleWalkingRunning(left, right);

            //Jumping
            HandleJumping(jump);
        }
        
        if (crouch && !alreadyGroundpounded) {
            HandleGroundpoundStart(left, right);
        }
        HandleGroundpound();

        //slow-rise check
        if (flying) {
            body.gravityScale = flyingGravity;
        } else {
            float gravityModifier = (state != Enums.PowerupState.Mini ? 1f : 0.4f);
            if (body.velocity.y > 2.5) {
                if (jump || jumpHeld) {
                    body.gravityScale = slowriseGravity;
                } else {
                    body.gravityScale = normalGravity * 1.5f * gravityModifier;
                }
            } else if (onGround || groundpound) {
                body.gravityScale = 0f;
            } else {
                body.gravityScale = normalGravity * (gravityModifier / 1.2f);
            }
        }

        if (groundpound && groundpoundCounter <= 0) {
            body.velocity = new Vector2(0f, -groundpoundVelocity);
        }

        if (!inShell && onGround && !(sliding && Mathf.Abs(floorAngle) > 20)) {
            bool abovemax;
            float invincibleSpeedBoost = (invincible > 0 ? 2f : 1);
            bool uphill = Mathf.Sign(floorAngle) == Mathf.Sign(body.velocity.x);
            float max = (functionallyRunning ? runningMaxSpeed : walkingMaxSpeed) * invincibleSpeedBoost * (uphill ? (1-(Mathf.Abs(floorAngle)/270f)) : 1);
            if (!sliding && left && !crouching) {
                abovemax = body.velocity.x < -max; 
            } else if (!sliding && right && !crouching) {
                abovemax = body.velocity.x > max;
            } else if (Mathf.Abs(floorAngle) > 40) {
                abovemax = Mathf.Abs(body.velocity.x) > (Mathf.Abs(floorAngle) / 30f);
            } else {
                abovemax = true;
            }
            //Friction...
            if (abovemax) {
                body.velocity *= 1-(delta * tileFriction * (knockback ? 3f : 4f) * (sliding ? 0.7f : 1f));
                if (Mathf.Abs(body.velocity.x) < 0.15f) {
                    body.velocity = new Vector2(0, body.velocity.y);
                }
            }
        }
        //Terminal velocity
        float terminalVelocityModifier = (state == Enums.PowerupState.Mini ? 0.65f : 1f);
        if (flying) {
            if (drill) {
                body.velocity = new Vector2(Mathf.Max(-1.5f, Mathf.Min(1.5f, body.velocity.x)), -drillVelocity);
            } else {
                body.velocity = new Vector2(Mathf.Max(-walkingMaxSpeed, Mathf.Min(walkingMaxSpeed, body.velocity.x)), Mathf.Max(body.velocity.y, -flyingTerminalVelocity));
            }
        } else if (!groundpound) { 
            body.velocity = new Vector2(body.velocity.x, Mathf.Max(body.velocity.y, terminalVelocity * terminalVelocityModifier));
        }
        if (!onGround) {
            body.velocity = new Vector2(Mathf.Max(-runningMaxSpeed * 1.2f, Mathf.Min(runningMaxSpeed * 1.2f, body.velocity.x)), body.velocity.y);
        }

        HandleSlopes();
        HandleSliding(up);
    }

    void HandleGroundpoundStart(bool left, bool right) {
        if (onGround || knockback || groundpound || drill 
            || holding || crouching || sliding 
            || onLeft || onRight) return;
        if (!flying && (left || right)) return;
        if (groundpoundDelay > 0) return;

        if (flying) {
            //start drill
            if (body.velocity.y < 0) {
                drill = true;
                hitBlock = true;
            }
        } else {
            //start groundpound
            //check if high enough above ground
            if (Physics2D.Raycast(body.position, Vector2.down, 0.25f * (state == Enums.PowerupState.Giant ? 2.5f : 1), ANY_GROUND_MASK)) return;
            
            onLeft = false;
            onRight = false;
            groundpound = true;
            singlejump = false;
            doublejump = false;
            triplejump = false;
            hitBlock = true;
            sliding = false;
            body.velocity = Vector2.zero;
            groundpoundCounter = groundpoundTime * (state == Enums.PowerupState.Giant ? 1.5f : 1);
            photonView.RPC("PlaySound", RpcTarget.All, "player/groundpound");
            alreadyGroundpounded = true;
            groundpoundDelay = 0.7f;
        }
    }

    void HandleGroundpound() {
        if (!(photonView.IsMine && onGround && (groundpound || drill) && hitBlock)) return;
        bool tempHitBlock = false;
        foreach (Vector3Int tile in tilesStandingOn) {
            int temp = InteractWithTile(tile, InteractableTile.InteractionDirection.Down);
            if (temp != -1) {
                tempHitBlock |= temp == 1;
            }
        }
        hitBlock = tempHitBlock;
        if (drill) {
            flying = hitBlock;
            drill = hitBlock;
            if (hitBlock) {
                onGround = false;
            }
        } else {
            //groundpound
            if (hitBlock) {
                koyoteTime = 1.5f;
            } else {
                photonView.RPC("PlaySound", RpcTarget.All, "player/groundpound-landing" + (state == Enums.PowerupState.Mini ? "-mini" : ""));
                if (state == Enums.PowerupState.Giant) {
                    photonView.RPC("PlaySound", RpcTarget.All, "player/groundpound_mega");
                    cameraController.screenShakeTimer = 0.35f;
                }
                photonView.RPC("SpawnParticle", RpcTarget.All, "Prefabs/Particle/GroundpoundDust");
            }
        }
    }
    void OnDrawGizmos() {
        Gizmos.DrawRay(body.position, body.velocity);
        Gizmos.DrawCube(body.position + new Vector2(0, hitboxes[0].size.y/2f * transform.lossyScale.y) + (body.velocity * Time.fixedDeltaTime), hitboxes[0].size * transform.lossyScale);
    }
}