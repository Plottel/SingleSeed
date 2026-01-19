using Photon.Deterministic;
using UnityEditor.Build;
using UnityEngine.Scripting;

namespace Quantum.Platformer
{
	/// <summary>
	/// Core gameplay system that handles player lifecycle, coin collection, and win conditions
	/// for the platformer example.
	/// </summary>
	[Preserve]
	public unsafe class GameplaySystem : SystemMainThread, ISignalOnPlayerAdded, ISignalOnPlayerRemoved,
		ISignalPlayerFell, ISignalOnTrigger3D
	{
		private static readonly FP TileSize = FP._1;
		private static readonly FPVector3 GridOrigin = FPVector3.Zero;

		public EntityPrototype GreenBeanPrototype;

        public override void OnInit(Frame f)
        {
			GreenBeanPrototype = f.FindAsset<EntityPrototype>("Prefabs/GreenBeanEntityPrototype");
        }

		public override void Update(Frame frame)
		{
			var gameplay = frame.Unsafe.GetPointerSingleton<PlatformerGameplay>();

			var growables = frame.Unsafe.GetComponentBlockIterator<QGrowable>();
			foreach (var growablePair in growables)
			{
				EntityRef entity = growablePair.Entity;
				QGrowable* growable = growablePair.Component;

				growable->Age += 1;

				if (growable->Age >= 500) growable->Stage = GrowableStage.Established;
				else if (growable->Age >= 300) growable->Stage = GrowableStage.Seedling;
				else if (growable->Age >= 100) growable->Stage = GrowableStage.Emerging;
				else growable->Stage = GrowableStage.Seed;

			}

			foreach (var playerLinkPair in frame.GetComponentIterator<PlayerLink>())
			{
				var input = frame.GetPlayerInput(playerLinkPair.Component.PlayerRef);
				if (input->Fire.WasPressed == false)
					continue;

				var plantPosition = SnapToTileCenter(input->PlantPosition);
				if (IsTileOccupied(frame, plantPosition))
					continue;

				EntityRef bean = frame.Create(GreenBeanPrototype);
				frame.Unsafe.GetPointer<Transform3D>(bean)->Position = plantPosition;
			}

			// Reset game state when game over timer expires
			if (gameplay->GameOverTimer.HasStoppedThisFrame(frame))
			{
				gameplay->GameOverTimer = default;
				gameplay->Winner = default;

				foreach (var pair in frame.Unsafe.GetComponentBlockIterator<PlatformerPlayer>())
				{
					pair.Component->CollectedCoins = 0;
					RespawnPlayer(frame, pair.Entity);
				}

				// Enable player movement
				frame.SystemEnable<KCCSystem>();
			}
		}

		void ISignalOnPlayerAdded.OnPlayerAdded(Frame frame, PlayerRef playerRef, bool firstTime)
		{
			var runtimePlayer = frame.GetPlayerData(playerRef);
			var playerEntity = frame.Create(runtimePlayer.PlayerAvatar);

			frame.AddOrGet<PlayerLink>(playerEntity, out var playerLink);
			playerLink->PlayerRef = playerRef;

			RespawnPlayer(frame, playerEntity);
		}

		void ISignalOnPlayerRemoved.OnPlayerRemoved(Frame frame, PlayerRef playerRef)
		{
			foreach (var pair in frame.GetComponentIterator<PlayerLink>())
			{
				if (pair.Component.PlayerRef != playerRef)
					continue;

				// Destroy player entity
				frame.Destroy(pair.Entity);
			}
		}

		void ISignalPlayerFell.PlayerFell(Frame frame, EntityRef entity)
		{
			RespawnPlayer(frame, entity);
		}

		void ISignalOnTrigger3D.OnTrigger3D(Frame frame, TriggerInfo3D info)
		{
			if (frame.Unsafe.TryGetPointer<PlatformerPlayer>(info.Other, out var player) == false)
				return;

			// Handle coin collection and flag (finish line) triggers
			if (frame.Has<Coin>(info.Entity))
			{
				player->CollectedCoins++;

				frame.Signals.CoinCollected(info.Entity);
				frame.Events.CoinCollected(info.Entity, info.Other);
			}
			else if (frame.Has<Flag>(info.Entity))
			{
				CheckGameOver(frame, info.Other, player);
			}
		}

		private void RespawnPlayer(Frame frame, EntityRef entity)
		{
			var spawnData = GetSpawnData(frame);

			var kcc = frame.Unsafe.GetPointer<KCC>(entity);

			// Reset player position, rotation and velocity
			kcc->Teleport(frame, spawnData.Position);
			kcc->SetLookRotation(spawnData.Rotation);
			kcc->Data.KinematicVelocity = default;

			// Notify clients about the new look rotation
			var playerLink = frame.Unsafe.GetPointer<PlayerLink>(entity);
			frame.Events.ResetLookRotation(playerLink->PlayerRef, kcc->GetLookRotation());
		}

		private void CheckGameOver(Frame frame, EntityRef playerEntity, PlatformerPlayer* player)
		{
			var gameplay = frame.Unsafe.GetPointerSingleton<PlatformerGameplay>();

			if (gameplay->Winner.IsValid)
				return; // Someone else was first

			if (player->CollectedCoins < gameplay->MinCoinsToWin)
				return; // Not enough coins

			// Set winner and start game over countdown
			gameplay->Winner = frame.Unsafe.GetPointer<PlayerLink>(playerEntity)->PlayerRef;
			gameplay->GameOverTimer = FrameTimer.FromSeconds(frame, gameplay->GameOverTime);

			// Stop player movement
			frame.SystemDisable<KCCSystem>();
		}

		private (FPVector3 Position, FPQuaternion Rotation) GetSpawnData(Frame frame)
		{
			var gameplay = frame.Unsafe.GetPointerSingleton<PlatformerGameplay>();

			// Randomize spawn position within a circle around the base spawn point
			var position = gameplay->SpawnPosition + frame.RNG->InUnitCircle(true).XOY * gameplay->SpawnRadius;
			var rotation = FPQuaternion.Euler(18, 90, 0);

			return (position, rotation);
		}

		private static FPVector3 SnapToTileCenter(FPVector3 position)
		{
			var relative = position - GridOrigin;
			var tileX = FPMath.FloorToInt(relative.X / TileSize);
			var tileZ = FPMath.FloorToInt(relative.Z / TileSize);

			return new FPVector3(
				GridOrigin.X + (tileX + FP._0_50) * TileSize,
				GridOrigin.Y,
				GridOrigin.Z + (tileZ + FP._0_50) * TileSize);
		}

		private static bool IsTileOccupied(Frame frame, FPVector3 tileCenter)
		{
			var growables = frame.Unsafe.GetComponentBlockIterator<QGrowable>();
			foreach (var growablePair in growables)
			{
				var transform = frame.Unsafe.GetPointer<Transform3D>(growablePair.Entity);
				if (transform->Position == tileCenter)
					return true;
			}

			return false;
		}
	}
}
