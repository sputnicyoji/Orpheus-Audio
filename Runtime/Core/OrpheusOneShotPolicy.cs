namespace Orpheus.Audio.Core
{
    internal enum OrpheusOneShotAdmissionStage : byte
    {
        MainThread = 0,
        Availability = 1,
        Key = 2,
        PlaybackKind = 3,
        Position = 4,
        Activation = 5,
        Transport = 6,
        Load = 7,
        Cooldown = 8,
        Distance = 9,
        Polyphony = 10,
        PoolCapacity = 11,
        Complete = 12
    }

    internal enum OrpheusOneShotAdmissionResult : byte
    {
        Pending = 0,
        Accepted = 1,
        WrongThreadRejected = 2,
        UnavailableRejected = 3,
        InvalidKeyRejected = 4,
        PlaybackKindRejected = 5,
        InvalidPositionRejected = 6,
        PreReadyRejected = 7,
        SuspendedRejected = 8,
        LoadNotReadyRejected = 9,
        LoadFailed = 10,
        CooldownRejected = 11,
        DistanceRejected = 12,
        PolyphonyRejected = 13,
        PoolCapacityRejected = 14,
        RuntimeInvalid = 15
    }

    internal struct OrpheusOneShotAdmission
    {
        private byte _nextStage;
        private OrpheusOneShotAdmissionResult _result;

        internal OrpheusOneShotAdmissionResult Result => _result;

        internal bool Pass(OrpheusOneShotAdmissionStage stage, bool passed)
        {
            if (_result != OrpheusOneShotAdmissionResult.Pending ||
                (byte)stage != _nextStage || stage == OrpheusOneShotAdmissionStage.Load ||
                stage == OrpheusOneShotAdmissionStage.Complete)
            {
                return false;
            }

            if (!passed)
            {
                _result = GetFailure(stage);
                return false;
            }

            Advance();
            return true;
        }

        internal bool PassLoad(OrpheusClipLoadState loadState)
        {
            if (_result != OrpheusOneShotAdmissionResult.Pending ||
                _nextStage != (byte)OrpheusOneShotAdmissionStage.Load)
            {
                return false;
            }

            switch (loadState)
            {
                case OrpheusClipLoadState.Loaded:
                    Advance();
                    return true;
                case OrpheusClipLoadState.Failed:
                    _result = OrpheusOneShotAdmissionResult.LoadFailed;
                    return false;
                case OrpheusClipLoadState.Unloaded:
                case OrpheusClipLoadState.Loading:
                    _result = OrpheusOneShotAdmissionResult.LoadNotReadyRejected;
                    return false;
                default:
                    _result = OrpheusOneShotAdmissionResult.RuntimeInvalid;
                    return false;
            }
        }

        private void Advance()
        {
            _nextStage++;
            if (_nextStage == (byte)OrpheusOneShotAdmissionStage.Complete)
            {
                _result = OrpheusOneShotAdmissionResult.Accepted;
            }
        }

        private static OrpheusOneShotAdmissionResult GetFailure(
            OrpheusOneShotAdmissionStage stage)
        {
            switch (stage)
            {
                case OrpheusOneShotAdmissionStage.MainThread:
                    return OrpheusOneShotAdmissionResult.WrongThreadRejected;
                case OrpheusOneShotAdmissionStage.Availability:
                    return OrpheusOneShotAdmissionResult.UnavailableRejected;
                case OrpheusOneShotAdmissionStage.Key:
                    return OrpheusOneShotAdmissionResult.InvalidKeyRejected;
                case OrpheusOneShotAdmissionStage.PlaybackKind:
                    return OrpheusOneShotAdmissionResult.PlaybackKindRejected;
                case OrpheusOneShotAdmissionStage.Position:
                    return OrpheusOneShotAdmissionResult.InvalidPositionRejected;
                case OrpheusOneShotAdmissionStage.Activation:
                    return OrpheusOneShotAdmissionResult.PreReadyRejected;
                case OrpheusOneShotAdmissionStage.Transport:
                    return OrpheusOneShotAdmissionResult.SuspendedRejected;
                case OrpheusOneShotAdmissionStage.Cooldown:
                    return OrpheusOneShotAdmissionResult.CooldownRejected;
                case OrpheusOneShotAdmissionStage.Distance:
                    return OrpheusOneShotAdmissionResult.DistanceRejected;
                case OrpheusOneShotAdmissionStage.Polyphony:
                    return OrpheusOneShotAdmissionResult.PolyphonyRejected;
                case OrpheusOneShotAdmissionStage.PoolCapacity:
                    return OrpheusOneShotAdmissionResult.PoolCapacityRejected;
                default:
                    return OrpheusOneShotAdmissionResult.RuntimeInvalid;
            }
        }
    }

    internal readonly struct OrpheusPosition3
    {
        internal OrpheusPosition3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }

        internal bool IsFinite =>
            !float.IsNaN(X) && !float.IsInfinity(X) &&
            !float.IsNaN(Y) && !float.IsInfinity(Y) &&
            !float.IsNaN(Z) && !float.IsInfinity(Z);
    }

    internal readonly struct OrpheusSpatialOneShotState
    {
        internal OrpheusSpatialOneShotState(
            OrpheusPosition3 position,
            double maximumDistanceSquared)
        {
            Position = position;
            MaximumDistanceSquared = maximumDistanceSquared;
        }

        internal OrpheusPosition3 Position { get; }
        internal double MaximumDistanceSquared { get; }
    }

    internal enum OrpheusOneShotSlotState : byte
    {
        Free = 0,
        PlayingOneShot = 1,
        FadingOut = 2
    }

    internal struct OrpheusOneShotSlot
    {
        private OrpheusOneShotSlotState _state;
        private OrpheusAudioKey _key;
        private OrpheusCategory _category;
        private byte _priority;
        private float _activeElapsed;
        private float _logicalDuration;
        private OrpheusAudioKey _pendingKey;
        private OrpheusCategory _pendingCategory;
        private byte _pendingPriority;
        private OrpheusSpatialOneShotState _spatialState;
        private OrpheusSpatialOneShotState _pendingSpatialState;

        internal OrpheusOneShotSlotState State => _state;
        internal OrpheusAudioKey Key => _key;
        internal OrpheusCategory Category => _category;
        internal byte Priority => _priority;
        internal float ActiveElapsed => _activeElapsed;
        internal float LogicalDuration => _logicalDuration;
        internal bool HasPending => _pendingKey.IsValid;
        internal OrpheusAudioKey PendingKey => _pendingKey;
        internal OrpheusCategory PendingCategory => _pendingCategory;
        internal byte PendingPriority => _pendingPriority;
        internal OrpheusPosition3 Position => _spatialState.Position;
        internal double MaximumDistanceSquared => _spatialState.MaximumDistanceSquared;
        internal OrpheusPosition3 PendingPosition => _pendingSpatialState.Position;
        internal double PendingMaximumDistanceSquared =>
            _pendingSpatialState.MaximumDistanceSquared;

        internal void Start(OrpheusAudioKey key, float logicalDuration)
        {
            Start(key, OrpheusCategory.Invalid, byte.MaxValue, logicalDuration);
        }

        internal void Start(
            OrpheusAudioKey key,
            OrpheusCategory category,
            byte priority,
            float logicalDuration)
        {
            Start(key, category, priority, logicalDuration, default);
        }

        internal void Start(
            OrpheusAudioKey key,
            OrpheusCategory category,
            byte priority,
            float logicalDuration,
            OrpheusSpatialOneShotState spatialState)
        {
            _state = OrpheusOneShotSlotState.PlayingOneShot;
            _key = key;
            _category = category;
            _priority = priority;
            _activeElapsed = 0f;
            _logicalDuration = logicalDuration;
            _pendingKey = OrpheusAudioKey.Invalid;
            _pendingCategory = OrpheusCategory.Invalid;
            _pendingPriority = 0;
            _spatialState = spatialState;
            _pendingSpatialState = default;
        }

        internal bool Advance(float activeDelta)
        {
            if (_state != OrpheusOneShotSlotState.PlayingOneShot || !(activeDelta > 0f))
            {
                return false;
            }

            _activeElapsed += activeDelta;
            return _activeElapsed >= _logicalDuration;
        }

        internal bool ScheduleReplacement(
            OrpheusAudioKey key,
            OrpheusCategory category,
            byte priority)
        {
            return ScheduleReplacement(key, category, priority, default);
        }

        internal bool ScheduleReplacement(
            OrpheusAudioKey key,
            OrpheusCategory category,
            byte priority,
            OrpheusSpatialOneShotState spatialState)
        {
            if (_state != OrpheusOneShotSlotState.PlayingOneShot || !key.IsValid)
            {
                return false;
            }

            _state = OrpheusOneShotSlotState.FadingOut;
            _pendingKey = key;
            _pendingCategory = category;
            _pendingPriority = priority;
            _pendingSpatialState = spatialState;
            return true;
        }

        internal void StartPending(float logicalDuration)
        {
            if (_state != OrpheusOneShotSlotState.FadingOut || !_pendingKey.IsValid)
            {
                return;
            }

            Start(
                _pendingKey,
                _pendingCategory,
                _pendingPriority,
                logicalDuration,
                _pendingSpatialState);
        }

        internal bool BeginFadeOut()
        {
            if (_state != OrpheusOneShotSlotState.PlayingOneShot)
            {
                return false;
            }

            _state = OrpheusOneShotSlotState.FadingOut;
            ClearPendingIdentity();
            return true;
        }

        internal bool CancelPending()
        {
            if (_state != OrpheusOneShotSlotState.FadingOut || !_pendingKey.IsValid)
            {
                return false;
            }

            ClearPendingIdentity();
            return true;
        }

        internal void Clear()
        {
            this = default;
        }

        private void ClearPendingIdentity()
        {
            _pendingKey = OrpheusAudioKey.Invalid;
            _pendingCategory = OrpheusCategory.Invalid;
            _pendingPriority = 0;
            _pendingSpatialState = default;
        }
    }

    internal static class OrpheusOneShotPolicy
    {
        internal const float MinimumStealAgeSeconds = 0.05f;
        internal const float FadeDurationSeconds = 0.015f;
        internal const int MaximumProjectedNonUi2D = 2;

        internal static int FindFirstFree(OrpheusOneShotSlot[] slots)
        {
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                if (slots[slotIndex].State == OrpheusOneShotSlotState.Free)
                {
                    return slotIndex;
                }
            }

            return -1;
        }

        internal static int CountFutureKey(
            OrpheusOneShotSlot[] slots,
            OrpheusAudioKey key)
        {
            var count = 0;
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (slot.Key == key || slot.PendingKey == key)
                {
                    count++;
                }
            }

            return count;
        }

        internal static int CountProjectedNonUi(OrpheusOneShotSlot[] slots)
        {
            var count = 0;
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (slot.State == OrpheusOneShotSlotState.Free)
                {
                    continue;
                }

                var category = slot.HasPending ? slot.PendingCategory : slot.Category;
                if (!IsUi(category))
                {
                    count++;
                }
            }

            return count;
        }

        internal static int FindSameKeyVictim(
            OrpheusOneShotSlot[] slots,
            OrpheusAudioKey key)
        {
            var selectedIndex = -1;
            var selectedAge = 0f;
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (slot.State != OrpheusOneShotSlotState.PlayingOneShot ||
                    slot.Key != key ||
                    slot.ActiveElapsed < MinimumStealAgeSeconds)
                {
                    continue;
                }

                if (selectedIndex < 0 || slot.ActiveElapsed > selectedAge)
                {
                    selectedIndex = slotIndex;
                    selectedAge = slot.ActiveElapsed;
                }
            }

            return selectedIndex;
        }

        internal static int FindAdmissionSlot(
            OrpheusOneShotSlot[] slots,
            OrpheusCategory requestCategory,
            byte requestPriority)
        {
            var requestIsUi = IsUi(requestCategory);
            var projectedNonUi = CountProjectedNonUi(slots);
            // Spec: projected non-UI occupancy cannot exceed 2. Steal of non-UI by
            // non-UI preserves count, so an already-broken over-cap must fail closed.
            if (!requestIsUi && projectedNonUi > MaximumProjectedNonUi2D)
            {
                return -1;
            }

            if (requestIsUi || projectedNonUi < MaximumProjectedNonUi2D)
            {
                var freeIndex = FindFirstFree(slots);
                if (freeIndex >= 0)
                {
                    return freeIndex;
                }
            }

            var selectedIndex = -1;
            var selectedPriority = (byte)0;
            var selectedAge = 0f;
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (slot.State != OrpheusOneShotSlotState.PlayingOneShot ||
                    slot.ActiveElapsed < MinimumStealAgeSeconds ||
                    slot.Priority < requestPriority)
                {
                    continue;
                }

                if (!requestIsUi && IsUi(slot.Category))
                {
                    continue;
                }

                if (IsBetterOrdinaryVictim(
                        slot.Priority,
                        0d,
                        slot.ActiveElapsed,
                        slotIndex,
                        selectedPriority,
                        0d,
                        selectedAge,
                        selectedIndex))
                {
                    selectedIndex = slotIndex;
                    selectedPriority = slot.Priority;
                    selectedAge = slot.ActiveElapsed;
                }
            }

            return selectedIndex;
        }

        internal static double CalculateMaximumDistanceSquared(float maximumDistance)
        {
            return (double)maximumDistance * maximumDistance;
        }

        internal static double CalculateSquaredDistance(
            OrpheusPosition3 source,
            OrpheusPosition3 listener)
        {
            var deltaX = (double)source.X - listener.X;
            var deltaY = (double)source.Y - listener.Y;
            var deltaZ = (double)source.Z - listener.Z;
            return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
        }

        internal static bool IsWithinMaximumDistance(
            double squaredDistance,
            double maximumDistanceSquared)
        {
            return squaredDistance <= maximumDistanceSquared;
        }

        internal static double CalculateDistanceRatio(
            double squaredDistance,
            double maximumDistanceSquared)
        {
            return squaredDistance / maximumDistanceSquared;
        }

        internal static int Find3DAdmissionSlot(
            OrpheusOneShotSlot[] slots,
            byte requestPriority,
            OrpheusPosition3 listenerPosition,
            double[] distanceRatios)
        {
            Overwrite3DDistanceRatios(slots, listenerPosition, distanceRatios);
            return Find3DAdmissionSlot(slots, requestPriority, distanceRatios);
        }

        internal static void Overwrite3DDistanceRatios(
            OrpheusOneShotSlot[] slots,
            OrpheusPosition3 listenerPosition,
            double[] distanceRatios)
        {
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                distanceRatios[slotIndex] = slot.State == OrpheusOneShotSlotState.PlayingOneShot
                    ? CalculateDistanceRatio(
                        CalculateSquaredDistance(slot.Position, listenerPosition),
                        slot.MaximumDistanceSquared)
                    : 0d;
            }
        }

        internal static int Find3DAdmissionSlot(
            OrpheusOneShotSlot[] slots,
            byte requestPriority,
            double[] distanceRatios)
        {
            var freeIndex = FindFirstFree(slots);
            if (freeIndex >= 0)
            {
                return freeIndex;
            }

            var selectedIndex = -1;
            var selectedPriority = (byte)0;
            var selectedDistanceRatio = 0d;
            var selectedAge = 0f;
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var slot = slots[slotIndex];
                if (slot.State != OrpheusOneShotSlotState.PlayingOneShot ||
                    slot.ActiveElapsed < MinimumStealAgeSeconds ||
                    slot.Priority < requestPriority)
                {
                    continue;
                }

                var distanceRatio = distanceRatios[slotIndex];
                if (IsBetterOrdinaryVictim(
                        slot.Priority,
                        distanceRatio,
                        slot.ActiveElapsed,
                        slotIndex,
                        selectedPriority,
                        selectedDistanceRatio,
                        selectedAge,
                        selectedIndex))
                {
                    selectedIndex = slotIndex;
                    selectedPriority = slot.Priority;
                    selectedDistanceRatio = distanceRatio;
                    selectedAge = slot.ActiveElapsed;
                }
            }

            return selectedIndex;
        }

        private static bool IsBetterOrdinaryVictim(
            byte priority,
            double distanceRatio,
            float age,
            int slotIndex,
            byte selectedPriority,
            double selectedDistanceRatio,
            float selectedAge,
            int selectedIndex)
        {
            return selectedIndex < 0 ||
                   priority > selectedPriority ||
                   (priority == selectedPriority &&
                    (distanceRatio > selectedDistanceRatio ||
                     (distanceRatio == selectedDistanceRatio &&
                      (age > selectedAge ||
                       (age == selectedAge && slotIndex < selectedIndex)))));
        }

        private static bool IsUi(OrpheusCategory category)
        {
            return category == OrpheusCategory.SfxUi;
        }
    }
}
