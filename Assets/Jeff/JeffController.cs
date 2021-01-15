using UnityEngine;
using UnityEngine.InputSystem;

namespace Jeff
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class JeffController : MonoBehaviour
    {
        [Header("Walking")] 
        [SerializeField] private float maxWalkCos = 0.5f;
        [SerializeField] private float walkSpeed = 7;

        [Header("Jumping")]
        [SerializeField] private float firstJumpSpeed = 8;
        [SerializeField] private float jumpSpeed = 3;
        [SerializeField] private float fallSpeed = 12;
        [SerializeField] private int numberOfJumps = 2;
        [SerializeField] private AnimationCurve jumpFallOff = AnimationCurve.Linear(0, 1, 1, 0);
        [SerializeField] private FixedStopwatch jumpStopwatch = new FixedStopwatch();
        
        [Header("Getting a whooping")] 
        [SerializeField] private Vector2 bounceBackStrength = new Vector2(8, 12);
        [SerializeField] private FixedStopwatch hitStopwatch = new FixedStopwatch();

        [Header("Giving a whooping")]
        [SerializeField] private float attackSpeed = 12;
        [SerializeField] private FixedStopwatch attackStopwatch = new FixedStopwatch();
        
        public Vector2 DesiredMovementDirection { get; private set; }
        public bool WantsToJump { get; private set; }
        public JeffState State { get; private set; } = JeffState.Movement;
        public int FacingDirection { get; private set; } = 1;
        
        public bool IsGrounded => _groundContact.HasValue;
        public Vector2 Velocity => _rigidbody2D.velocity;
        public float AttackCompletion => attackStopwatch.Completion;
        public float JumpCompletion => jumpStopwatch.Completion;
        public bool IsJumping => !jumpStopwatch.IsFinished;
        public bool IsFirstJump => _jumpsLeft == numberOfJumps - 1;

        private Rigidbody2D _rigidbody2D;
        private ContactFilter2D _contactFilter;
        private ContactPoint2D? _groundContact;
        private ContactPoint2D? _ceilingContact;
        private ContactPoint2D? _wallContact;
        private readonly ContactPoint2D[] _contacts = new ContactPoint2D[16];

        private bool _wasOnTheGround;
        private int _jumpsLeft;
        private bool _canAttack;
        private int _enemyLayer;

        private void Awake()
        {
            _rigidbody2D = GetComponent<Rigidbody2D>();
            _enemyLayer = LayerMask.NameToLayer("Enemy");
            _contactFilter = new ContactFilter2D();
            _contactFilter.SetLayerMask(LayerMask.GetMask("Ground", "Enemy"));
        }

        public void OnMove(InputValue value)
        {
            DesiredMovementDirection = value.Get<Vector2>();
        }

        public void OnJump(InputValue value)
        {
            WantsToJump = value.Get<float>() > 0.5f;
            if (WantsToJump && State == JeffState.Movement && _jumpsLeft > 0 && jumpStopwatch.IsReady)
            {
                _jumpsLeft--;
                jumpStopwatch.Split();
            } 
            
            if (!WantsToJump && _jumpsLeft == 0)
            {
                jumpStopwatch.Reset();
            }
        }

        public void OnAttack(InputValue value)
        {
            EnterAttackState();
        }
        
        private void OnCollisionEnter2D(Collision2D other)
        {
            if (other.gameObject.layer != _enemyLayer) return;

            var relativePosition = (Vector2) transform.InverseTransformPoint(other.transform.position);
            var direction = (_rigidbody2D.centerOfMass - relativePosition).normalized;
            EnterHitState(direction);
        }

        private void Recalculate()
        {
            if (_rigidbody2D.velocity.x > 0.1f)
                FacingDirection = 1;
            else if (_rigidbody2D.velocity.x < -0.1f)
                FacingDirection = -1;

            _groundContact = null;
            _ceilingContact = null;
            _wallContact = null;

            float groundProjection = maxWalkCos;
            float wallProjection = maxWalkCos;
            float ceilingProjection = -maxWalkCos;

            int numberOfContacts = _rigidbody2D.GetContacts(_contactFilter, _contacts);
            for (var i = 0; i < numberOfContacts; i++)
            {
                var contact = _contacts[i];
                float projection = Vector2.Dot(Vector2.up, contact.normal);

                if (projection > groundProjection)
                {
                    _groundContact = contact;
                    groundProjection = projection;
                }
                else if (projection < ceilingProjection)
                {
                    _ceilingContact = contact;
                    ceilingProjection = projection;
                }
                else if (projection <= wallProjection)
                {
                    _wallContact = contact;
                    wallProjection = projection;
                }
            }
        }

        private void FixedUpdate()
        {
            Recalculate();

            switch (State)
            {
                case JeffState.Movement:
                    UpdateMovementState();
                    break;
                case JeffState.Attack:
                    UpdateAttackState();
                    break;
                case JeffState.Hit:
                    UpdateHitState();
                    break;
            }
        }

        private void EnterHitState(Vector2 direction)
        {
            if (State != JeffState.Hit && !hitStopwatch.IsReady) return;
            State = JeffState.Hit;

            hitStopwatch.Split();
            _rigidbody2D.AddForce(
                direction * bounceBackStrength - _rigidbody2D.velocity,
                ForceMode2D.Impulse
            );
        }

        private void UpdateHitState()
        {
            _rigidbody2D.AddForce(Physics2D.gravity * 4);
            if (hitStopwatch.IsFinished && (_groundContact.HasValue || _wallContact.HasValue))
            {
                hitStopwatch.Split();
                EnterMovementState();
            }
        }

        private void EnterAttackState()
        {
            if (State != JeffState.Movement || !attackStopwatch.IsReady || !_canAttack) return;
            State = JeffState.Attack;

            attackStopwatch.Split();
            _canAttack = false;
        }

        private void UpdateAttackState()
        {
            _rigidbody2D.AddForce(
                new Vector2(FacingDirection * attackSpeed, 0) - _rigidbody2D.velocity,
                ForceMode2D.Impulse
            );
            if (attackStopwatch.IsFinished || _wallContact.HasValue)
            {
                attackStopwatch.Split();
                EnterMovementState();
            }
        }

        private void EnterMovementState()
        {
            State = JeffState.Movement;
        }
        
        private void UpdateMovementState()
        {
            var previousVelocity = _rigidbody2D.velocity;
            var velocityChange = Vector2.zero;

            if (WantsToJump && IsJumping)
            {
                _wasOnTheGround = false;
                float currentJumpSpeed = IsFirstJump ? firstJumpSpeed : jumpSpeed;
                currentJumpSpeed *= jumpFallOff.Evaluate(JumpCompletion);
                velocityChange.y = currentJumpSpeed - previousVelocity.y;
                
                if (_ceilingContact.HasValue)
                    jumpStopwatch.Reset();
            }
            else if (_groundContact.HasValue)
            {
                _jumpsLeft = numberOfJumps;
                _wasOnTheGround = true;
                _canAttack = true;
            }
            else
            {
                if (_wasOnTheGround)
                {
                    _jumpsLeft -= 1;
                    _wasOnTheGround = false;
                }
                
                velocityChange.y = (-fallSpeed - previousVelocity.y) / 8;
            }

            velocityChange.x = (DesiredMovementDirection.x * walkSpeed - previousVelocity.x) / 4;

            if (_wallContact.HasValue)
            {
                var wallDirection = (int) Mathf.Sign(_wallContact.Value.point.x - transform.position.x);
                var walkDirection = (int) Mathf.Sign(DesiredMovementDirection.x);

                if (walkDirection == wallDirection)
                    velocityChange.x = 0;
            }

            _rigidbody2D.AddForce(velocityChange, ForceMode2D.Impulse);
        }
    }
}