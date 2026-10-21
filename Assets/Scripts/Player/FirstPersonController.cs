using UnityEngine;
using Random = UnityEngine.Random;
using UnityStandardAssets.Utility;

namespace Brickcraft
{
    [RequireComponent(typeof (CharacterController))]
    [RequireComponent(typeof (AudioSource))]
    public class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private bool m_IsWalking;
        [SerializeField] private float m_WalkSpeed;
        [SerializeField] private float m_RunSpeed;
        [SerializeField] [Range(0f, 1f)] private float m_RunstepLenghten;
        public float m_JumpSpeed;
        public float m_StickToGroundForce;
        public float m_GravityMultiplier;
        [SerializeField] private MouseLook m_MouseLook;
        [SerializeField] private bool m_UseFovKick;
        [SerializeField] private FOVKick m_FovKick = new FOVKick();
        [SerializeField] private bool m_UseHeadBob;
        [SerializeField] private CurveControlledBob m_HeadBob = new CurveControlledBob();
        [SerializeField] private LerpControlledBob m_JumpBob = new LerpControlledBob();
        [SerializeField] private float m_StepInterval;
        [SerializeField] private AudioClip[] m_FootstepSounds;    // an array of footstep sounds that will be randomly selected from.
        [SerializeField] private AudioClip m_JumpSound;           // the sound played when character leaves the ground.
        [SerializeField] private AudioClip m_LandSound;           // the sound played when character touches back on ground.
        // what the player walks up without jumping: a brick or a block (0.478), not two
        [SerializeField] private float m_StepHeight = 0.5f;
        // how fast the camera catches up after stepping up, meters per second
        [SerializeField] private float m_StepSmoothing = 3f;
        // in water (see World.Water): swimming speed, going up (jump held), sinking (fully under, doing nothing),
        // how fast it gets to those speeds, how fast it climbs out at an edge, and walking with water to the knees
        [SerializeField] private float m_SwimSpeed = 2.2f;
        [SerializeField] private float m_SwimUpSpeed = 2.5f;
        [SerializeField] private float m_SinkSpeed = 0.6f;
        [SerializeField] private float m_SwimAcceleration = 8f;
        [SerializeField] private float m_ClimbOutSpeed = 5f;
        [SerializeField] private float m_WadingSpeedFactor = 0.7f;
        // how high over its feet the top of a bank can be, swimming, to climb out onto it (a sheer bank level with the
        // water, however deep: its head's above the water)
        [SerializeField] private float m_ClimbReach = 2f;

        /// <summary>How deep in water it is: not, to the knees (it wades), to the waist (it swims), or with its eyes under.</summary>
        public enum WaterDepth
        {
            None = 0,
            Feet = 1,
            Body = 2,
            Eyes = 3,
        }

        public WaterDepth Depth { get; private set; }

        /// <summary>In water to the waist or more: it swims.</summary>
        public bool IsSwimming {
            get { return Depth >= WaterDepth.Body; }
        }
        private float originalWalkSpeed;
        private float originalRunSpeed;

        private Camera m_Camera;
        private bool m_Jump;
        private float m_YRotation;
        private Vector2 m_Input;
        private Vector3 m_MoveDir = Vector3.zero;
        private CharacterController m_CharacterController;
        private CollisionFlags m_CollisionFlags;
        private bool m_PreviouslyGrounded;
        private Vector3 m_OriginalCameraPosition;
        private float m_StepCycle;
        private float m_NextStep;
        private bool m_Jumping;
        private AudioSource m_AudioSource;
        private float m_BaseVolume;

        // a push from a hit, fading out, and how fast it throws the player up (applied once, like a jump)
        private Vector3 m_Knockback;
        private float m_KnockbackUp;
        private const float KnockbackDrag = 12f;

        // stepping up moves the player at once: the camera starts where it was and follows (negative, going to 0)
        private float m_StepCameraOffset;

        // climbing out of the water onto a bank: the height its feet go up to (NaN while it isn't, see climbOut)
        private float m_ClimbTo = float.NaN;
        // how far ahead of it a bank is looked for, past its own radius
        private const float BankReach = 0.3f;

        private void Awake() {
            originalWalkSpeed = m_WalkSpeed;
            originalRunSpeed = m_RunSpeed;
        }

        private void Start()
        {
            m_CharacterController = GetComponent<CharacterController>();
            m_CharacterController.stepOffset = m_StepHeight;
            m_Camera = Camera.main;
            m_OriginalCameraPosition = m_Camera.transform.localPosition;
            m_FovKick.Setup(m_Camera);
            m_HeadBob.Setup(m_Camera, m_StepInterval);
            m_StepCycle = 0f;
            m_NextStep = m_StepCycle/2f;
            m_Jumping = false;
            m_AudioSource = GetComponent<AudioSource>();
            m_BaseVolume = m_AudioSource.volume;
            applyEffectsVolume();
            Brickcraft.Events.EventManager.SettingChanged.Subscribe(onSettingChanged);
			m_MouseLook.Init(transform , m_Camera.transform);
        }

        private void OnDestroy()
        {
            Brickcraft.Events.EventManager.SettingChanged.Unsubscribe(onSettingChanged);
        }

        // steps, jumps and landings are sound effects
        private void onSettingChanged(Brickcraft.Events.SettingChangedEvent e)
        {
            if (e.setting == GameSettings.EffectsVolumeSetting)
            {
                applyEffectsVolume();
            }
        }

        private void applyEffectsVolume()
        {
            m_AudioSource.volume = m_BaseVolume * GameSettings.EffectsVolume;
        }

        private void Update()
        {
            RotateView();
            // the jump state needs to read here to make sure it is not missed
            if (!m_Jump && !IsSwimming)
            {
                m_Jump = GameInput.GetButtonDown(GameInput.Jump);
            }
            UnderwaterView.Show(Depth == WaterDepth.Eyes);

            if (!m_PreviouslyGrounded && m_CharacterController.isGrounded)
            {
                StartCoroutine(m_JumpBob.DoBobCycle());
                PlayLandingSound();
                m_MoveDir.y = 0f;
                m_Jumping = false;
            }
            if (!m_CharacterController.isGrounded && !m_Jumping && m_PreviouslyGrounded)
            {
                m_MoveDir.y = 0f;
            }

            m_PreviouslyGrounded = m_CharacterController.isGrounded;
        }

        // how deep in water it is: its feet, its middle and its eyes (the camera)
        private void updateDepth()
        {
            WaterDepth before = Depth;
            Vector3 middle = transform.TransformPoint(m_CharacterController.center);
            if (World.Water.IsAt(m_Camera.transform.position))
                Depth = WaterDepth.Eyes;
            else if (World.Water.IsAt(middle))
                Depth = WaterDepth.Body;
            else if (World.Water.IsAt(middle + Vector3.down * (m_CharacterController.height / 2 - 0.15f)))
                Depth = WaterDepth.Feet;
            else
                Depth = WaterDepth.None;

            if (Depth >= WaterDepth.Body && before < WaterDepth.Body)
            {
                SoundManager.Instance.play(SoundManager.EFFECT_ENTER_WATER);
                m_Jump = false;
                m_Jumping = false;
            }
            if (Depth == WaterDepth.Eyes && before != WaterDepth.Eyes)
            {
                SoundManager.Instance.playOnce(SoundManager.EFFECT_SUBMERGE);
            }
        }

        // swimming into a bank whose top is within reach, with room on it: it climbs up the bank to its top, then onto it.
        // Letting go of forward stops it (it falls back in). False when it isn't climbing.
        private bool climbOut()
        {
            float feet = m_CharacterController.bounds.min.y;
            if (float.IsNaN(m_ClimbTo))
            {
                if (!IsSwimming || m_Input.y <= 0 || !bankAhead(feet, out float top))
                    return false;
                m_ClimbTo = top;
            }
            if (m_Input.y <= 0)
            {
                m_ClimbTo = float.NaN;
                return false;
            }
            Vector3 move;
            if (feet < m_ClimbTo + 0.02f)
            {
                move = Vector3.up*m_ClimbOutSpeed; // up the bank
            }
            else
            {
                // over its top: onto it, walking from there
                move = transform.forward*m_SwimSpeed;
                m_ClimbTo = float.NaN;
            }
            m_MoveDir = move;
            m_Jump = false;
            m_Jumping = false;
            m_CollisionFlags = m_CharacterController.Move(move*Time.fixedDeltaTime);
            m_StepCameraOffset = Mathf.MoveTowards(m_StepCameraOffset, 0, m_StepSmoothing*Time.fixedDeltaTime);
            UpdateCameraPosition(0);
            m_PreviouslyGrounded = m_CharacterController.isGrounded;
            return true;
        }

        // the top of what's right ahead (its feet over it), if it's a bank it can climb onto: within reach, flat, with room on it
        private bool bankAhead(float feet, out float top)
        {
            top = 0;
            float radius = m_CharacterController.radius;
            Vector3 ahead = transform.position + transform.forward*(radius + BankReach);
            Vector3 from = new Vector3(ahead.x, feet + m_ClimbReach, ahead.z);
            if (Physics.CheckSphere(from, 0.05f, ~0, QueryTriggerInteraction.Ignore))
                return false; // in what's ahead: too high
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, m_ClimbReach, ~0, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.normal.y < 0.7f)
                return false; // a slope
            top = hit.point.y;
            Vector3 bottom = new Vector3(ahead.x, top + radius + 0.05f, ahead.z);
            Vector3 head = bottom + Vector3.up*Mathf.Max(0, m_CharacterController.height - radius*2);
            foreach (Collider collider in Physics.OverlapCapsule(bottom, head, radius*0.9f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (collider.transform.root != transform.root)
                    return false; // no room
            }
            return true;
        }

        // in water: it moves where it looks once its eyes are under (along the ground otherwise), goes up while jump
        // is held (or swimming into a bank) and sinks slowly otherwise (at the surface too); at an edge, jump climbs out
        private void swim()
        {
            Vector3 forward = Depth == WaterDepth.Eyes ? m_Camera.transform.forward : transform.forward;
            Vector3 move = forward*m_Input.y + transform.right*m_Input.x;
            if (move.sqrMagnitude > 1)
                move.Normalize();
            Vector3 target = move*m_SwimSpeed;

            bool up = GameInput.GetButton(GameInput.Jump);
            bool atWall = (m_CollisionFlags & CollisionFlags.Sides) != 0;
            if (up && Depth != WaterDepth.Eyes && atWall)
                target.y = m_ClimbOutSpeed; // out over the edge
            else if (up || (atWall && m_Input.y > 0))
                target.y = m_SwimUpSpeed; // at the surface it bobs (up out of the water, and back in); along a bank swum into, up to its top to climb out (see climbOut)
            else
                target.y -= m_SinkSpeed;

            m_MoveDir = Vector3.MoveTowards(m_MoveDir, target, m_SwimAcceleration*Time.fixedDeltaTime);
            if (m_KnockbackUp > 0)
            {
                m_MoveDir.y = m_KnockbackUp;
                m_KnockbackUp = 0;
            }
            m_Jump = false;
            m_Jumping = false;
            m_CollisionFlags = m_CharacterController.Move((m_MoveDir + m_Knockback + current())*Time.fixedDeltaTime);
            m_Knockback = Vector3.MoveTowards(m_Knockback, Vector3.zero, KnockbackDrag*Time.fixedDeltaTime);
            m_StepCameraOffset = Mathf.MoveTowards(m_StepCameraOffset, 0, m_StepSmoothing*Time.fixedDeltaTime);
            UpdateCameraPosition(0);
            m_PreviouslyGrounded = m_CharacterController.isGrounded;
        }

        private void OnDisable()
        {
            UnderwaterView.Show(false);
        }

        // flowing water pushes the player along (see Water.CurrentAt): where its feet are, or its middle swimming
        private Vector3 current()
        {
            Vector3 feet = transform.position + Vector3.down * (m_CharacterController.height / 2 - 0.1f);
            Vector3 push = World.Water.CurrentAt(feet);
            return push != Vector3.zero ? push : World.Water.CurrentAt(transform.position);
        }

        /// <summary>Pushes the player (a hit): horizontally for a moment, and up like a small jump.</summary>
        public void Knockback(Vector3 velocity)
        {
            m_Knockback = new Vector3(velocity.x, 0, velocity.z);
            m_KnockbackUp = Mathf.Max(0, velocity.y);
        }

        public bool setOverWeightSpeed (bool isOverWeight) {
            // check if its already set
            if ((isOverWeight && m_WalkSpeed == 1) || (!isOverWeight && m_WalkSpeed == originalWalkSpeed)) {
                return false;
            }

            if (isOverWeight) {
                m_WalkSpeed = 1;
                m_RunSpeed = 1;
            } else {
                m_WalkSpeed = originalWalkSpeed;
                m_RunSpeed = originalRunSpeed;
            }

            return true;
        }

        private void PlayLandingSound()
        {
            m_AudioSource.clip = m_LandSound;
            m_AudioSource.Play();
            m_NextStep = m_StepCycle + .5f;
        }

        private void FixedUpdate()
        {
            // where the ground has no colliders yet (just teleported, respawned...) it would fall through it: the
            // player freezes until it has (see Player.waitForGround), but physics can step before that
            if (World.WorldBehaviour.Instance != null && !World.WorldBehaviour.Instance.IsWalkable(World.WorldBehaviour.ChunkAt(transform.position)))
            {
                m_MoveDir = Vector3.zero;
                return;
            }
            float speed;
            GetInput(out speed);
            updateDepth();
            if (climbOut())
            {
                return;
            }
            if (IsSwimming)
            {
                swim();
                return;
            }
            if (Depth == WaterDepth.Feet)
            {
                speed *= m_WadingSpeedFactor;
            }
            // always move along the camera forward as it is the direction that it being aimed at
            Vector3 desiredMove = transform.forward*m_Input.y + transform.right*m_Input.x;

            // get a normal for the surface that is being touched to move along it
            RaycastHit hitInfo;
            Physics.SphereCast(transform.position, m_CharacterController.radius, Vector3.down, out hitInfo,
                               m_CharacterController.height/2f, ~0, QueryTriggerInteraction.Ignore);
            desiredMove = Vector3.ProjectOnPlane(desiredMove, hitInfo.normal).normalized;

            m_MoveDir.x = desiredMove.x*speed;
            m_MoveDir.z = desiredMove.z*speed;


            if (m_KnockbackUp > 0)
            {
                m_MoveDir.y = m_KnockbackUp;
                m_KnockbackUp = 0;
                m_Jumping = true;
            }
            else if (m_CharacterController.isGrounded)
            {
                m_MoveDir.y = -m_StickToGroundForce;

                if (m_Jump)
                {
                    m_MoveDir.y = m_JumpSpeed;
                    PlayJumpSound();
                    m_Jump = false;
                    m_Jumping = true;
                }
            }
            else
            {
                m_MoveDir += Physics.gravity*m_GravityMultiplier*Time.fixedDeltaTime;
            }
            bool wasGrounded = m_CharacterController.isGrounded;
            float heightBefore = transform.position.y;
            m_CollisionFlags = m_CharacterController.Move((m_MoveDir + m_Knockback + current())*Time.fixedDeltaTime);
            // walked up a step (the controller lifted it, see m_StepHeight): the camera eases up instead of jumping
            float rise = transform.position.y - heightBefore;
            if (wasGrounded && !m_Jumping && rise > 0.1f) {
                m_StepCameraOffset = Mathf.Max(m_StepCameraOffset - rise, -m_StepHeight);
            }
            m_StepCameraOffset = Mathf.MoveTowards(m_StepCameraOffset, 0, m_StepSmoothing * Time.fixedDeltaTime);
            m_Knockback = Vector3.MoveTowards(m_Knockback, Vector3.zero, KnockbackDrag*Time.fixedDeltaTime);

            ProgressStepCycle(speed);
            UpdateCameraPosition(speed);

            m_MouseLook.UpdateCursorLock();
        }


        private void PlayJumpSound()
        {
            m_AudioSource.clip = m_JumpSound;
            m_AudioSource.Play();
        }


        private void ProgressStepCycle(float speed)
        {
            if (m_CharacterController.velocity.sqrMagnitude > 0 && (m_Input.x != 0 || m_Input.y != 0))
            {
                m_StepCycle += (m_CharacterController.velocity.magnitude + (speed*(m_IsWalking ? 1f : m_RunstepLenghten)))*
                             Time.fixedDeltaTime;
            }

            if (!(m_StepCycle > m_NextStep))
            {
                return;
            }

            m_NextStep = m_StepCycle + m_StepInterval;

            PlayFootStepAudio();
        }


        private void PlayFootStepAudio()
        {
            if (!m_CharacterController.isGrounded || m_FootstepSounds.Length == 0)
            {
                return;
            }
            // pick & play a random footstep sound from the array,
            // excluding sound at index 0
            int n = Random.Range(1, m_FootstepSounds.Length);
            m_AudioSource.clip = m_FootstepSounds[n];
            m_AudioSource.PlayOneShot(m_AudioSource.clip);
            // move picked sound to index 0 so it's not picked next time
            m_FootstepSounds[n] = m_FootstepSounds[0];
            m_FootstepSounds[0] = m_AudioSource.clip;
        }


        private void UpdateCameraPosition(float speed)
        {
            Vector3 newCameraPosition;
            if (!m_UseHeadBob)
            {
                newCameraPosition = m_OriginalCameraPosition;
                newCameraPosition.y += m_StepCameraOffset;
                m_Camera.transform.localPosition = newCameraPosition;
                return;
            }
            if (m_CharacterController.velocity.magnitude > 0 && m_CharacterController.isGrounded)
            {
                m_Camera.transform.localPosition =
                    m_HeadBob.DoHeadBob(m_CharacterController.velocity.magnitude +
                                      (speed*(m_IsWalking ? 1f : m_RunstepLenghten)));
                newCameraPosition = m_Camera.transform.localPosition;
                newCameraPosition.y = m_Camera.transform.localPosition.y - m_JumpBob.Offset();
            }
            else
            {
                newCameraPosition = m_Camera.transform.localPosition;
                newCameraPosition.y = m_OriginalCameraPosition.y - m_JumpBob.Offset();
            }
            newCameraPosition.y += m_StepCameraOffset;
            m_Camera.transform.localPosition = newCameraPosition;
        }


        private void GetInput(out float speed)
        {
            // Read input
            Vector2 movement = GameInput.GetMovement();
            float horizontal = movement.x;
            float vertical = movement.y;

            bool waswalking = m_IsWalking;

#if !MOBILE_INPUT
            // On standalone builds, walk/run speed is modified by a key press.
            // keep track of whether or not the character is walking or running
            m_IsWalking = !GameInput.GetButton(GameInput.Sprint);
#endif
            // set the desired speed to be walking or running
            speed = m_IsWalking ? m_WalkSpeed : m_RunSpeed;
            m_Input = new Vector2(horizontal, vertical);

            // normalize input if it exceeds 1 in combined length:
            if (m_Input.sqrMagnitude > 1)
            {
                m_Input.Normalize();
            }

            // handle speed change to give an fov kick
            // only if the player is going to a run, is running and the fovkick is to be used
            if (m_IsWalking != waswalking && m_UseFovKick && m_CharacterController.velocity.sqrMagnitude > 0)
            {
                StopAllCoroutines();
                StartCoroutine(!m_IsWalking ? m_FovKick.FOVKickUp() : m_FovKick.FOVKickDown());
            }
        }


        /// <summary>It looks where its body and camera point now (after something else turned them: a vehicle's seat).</summary>
        public void ResetLook()
        {
            if (m_Camera != null) // started
            {
                m_MouseLook.Init(transform, m_Camera.transform);
            }
        }

        private void RotateView()
        {
            m_MouseLook.LookRotation (transform, m_Camera.transform);
        }


        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            //dont move the rigidbody if the character is on top of it
            if (m_CollisionFlags == CollisionFlags.Below)
            {
                return;
            }

            if (body == null || body.isKinematic)
            {
                return;
            }
            body.AddForceAtPosition(m_CharacterController.velocity*0.1f, hit.point, ForceMode.Impulse);
        }
    }
}
