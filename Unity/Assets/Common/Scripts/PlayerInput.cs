using Photon.Deterministic;
using Quantum;
using UnityEngine;

namespace Starter
{
	/// <summary>
	/// PlayerInput handles accumulating player input from Unity and passes the accumulated input to Quantum.
	/// </summary>
	public class PlayerInput : QuantumEntityViewComponent
	{
		public Vector2 LookSensitivity = new Vector2(0.3f, 0.3f);
		public FPVector2 PitchClamp = new (-30, 70);
		public float TileSize = 1f;
		public Vector3 GridOrigin = Vector3.zero;
		public Transform TargetTileHighlight;
		public Renderer TargetTileRenderer;
		public Color TargetTileColor = Color.yellow;
		public float HighlightYOffset = 0.02f;

		public Vector2 LookRotation => _input.LookRotation.ToUnityVector2();

		private Quantum.Input _input;
		private InputActions _inputActions;
		private MaterialPropertyBlock _highlightPropertyBlock;

		public override void OnActivate(Frame frame)
		{
			var playerLink = GetPredictedQuantumComponent<PlayerLink>();
			if (Game.PlayerIsLocal(playerLink.PlayerRef) == false)
			{
				enabled = false;
				return;
			}

			// Register to Quantum input poll callback
			QuantumCallback.Subscribe(this, (CallbackPollInput callback) => PollInput(callback));

			QuantumEvent.Subscribe<EventResetLookRotation>(this, OnResetLookRotation);
		}

		// Accumulate input from Keyboard/Mouse. Input accumulation is mandatory (at least for look rotation) as Update can be
		// called multiple times before next PollInput is called - common if rendering speed is faster than Fusion simulation.
		public override void OnUpdateView()
		{
			var isCursorLocked = Cursor.lockState == CursorLockMode.Locked;

			// Accumulate input only if the cursor is locked.
			if (!isCursorLocked)
			{
				_input.MoveDirection = default;
			}
			else
			{
				var lookValue = _inputActions.Player.Look.ReadValue<Vector2>() * LookSensitivity;
				var lookRotationDelta = new Vector2(-lookValue.y, lookValue.x);

				#if UNITY_WEBGL && !UNITY_EDITOR
					if (Mathf.Abs(lookRotationDelta.x) > 45 || Mathf.Abs(lookRotationDelta.y) > 45)
					{
						// Prevent glitch in Chrome with high polling mice where cursor jumps rapidly from time to time
						lookRotationDelta = default;
					}

					// Sensitivity in WebGL builds on desktop is much higher for some reason, decrease it
					lookRotationDelta *= 0.5f;
				#endif

				_input.LookRotation = ClampLookRotation(_input.LookRotation + lookRotationDelta.ToFPVector2());
				_input.MoveDirection = _inputActions.Player.Move.ReadValue<Vector2>().ToFPVector2();
			}

			var fireRequested = _inputActions.Player.Attack.IsPressed();
			var targetTileCenter = GetTargetTileCenter();

			if (TargetTileHighlight != null)
			{
				TargetTileHighlight.position = targetTileCenter + Vector3.up * HighlightYOffset;
				if (!TargetTileHighlight.gameObject.activeSelf)
					TargetTileHighlight.gameObject.SetActive(true);
			}

			if (TargetTileRenderer != null)
			{
				_highlightPropertyBlock ??= new MaterialPropertyBlock();
				TargetTileRenderer.GetPropertyBlock(_highlightPropertyBlock);
				_highlightPropertyBlock.SetColor("_Color", TargetTileColor);
				TargetTileRenderer.SetPropertyBlock(_highlightPropertyBlock);
			}

			if (fireRequested)
			{
				_input.Fire = true;
				_input.PlantPosition = targetTileCenter.ToFPVector3();
			}
			else
			{
				_input.Fire = false;
				_input.PlantPosition = default;
			}

			_input.Jump = _inputActions.Player.Jump.IsPressed();
			_input.Sprint = _inputActions.Player.Sprint.IsPressed();
		}

		private void OnEnable()
		{
			// InputActions class is auto-generated from the InputSystem_Actions asset
			_inputActions ??= new InputActions();
			_inputActions.Enable();
		}

		private void OnDisable()
		{
			_inputActions.Disable();
		}

		// Quantum polls accumulated input. This callback can be executed multiple times in a row if there is a performance spike.
		private void PollInput(CallbackPollInput callback)
		{
			callback.SetInput(_input, DeterministicInputFlags.Repeatable);
		}

		private void OnResetLookRotation(EventResetLookRotation callback)
		{
			_input.LookRotation = callback.Look;
		}

		private FPVector2 ClampLookRotation(FPVector2 lookRotation)
		{
			lookRotation.X = FPMath.Clamp(lookRotation.X, PitchClamp.X, PitchClamp.Y);
			return lookRotation;
		}

		private Vector3 GetTargetTileCenter()
		{
			var forward = transform.forward;
			var forwardXZ = new Vector3(forward.x, 0f, forward.z);
			if (forwardXZ.sqrMagnitude < 0.001f)
				forwardXZ = Vector3.forward;

			forwardXZ.Normalize();

			var targetPoint = transform.position + forwardXZ * TileSize;
			var tileX = Mathf.FloorToInt((targetPoint.x - GridOrigin.x) / TileSize);
			var tileZ = Mathf.FloorToInt((targetPoint.z - GridOrigin.z) / TileSize);

			return new Vector3(
				GridOrigin.x + (tileX + 0.5f) * TileSize,
				GridOrigin.y,
				GridOrigin.z + (tileZ + 0.5f) * TileSize);
		}
	}
}
