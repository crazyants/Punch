using IdeaBlade.Core;
using IdeaBlade.EntityModel;

namespace Cocktail
{
    /// <summary>
    /// Provides extension methods to initialize and reset the DevForce Fake Backing Store
    /// </summary>
    public static partial class FakeStoreEntityManagerProviderFns
    {
        /// <summary>Initializes the fake backing store.</summary>
        public static OperationResult InitializeFakeBackingStoreAsync<T>(this IEntityManagerProvider<T> @this)
            where T : EntityManager
        {
            if (!@this.ConnectionOptions.IsFake)
            {
                DebugFns.WriteLine(StringResources.NonSuitableEmpForFakeStoreOperation);
                return AlwaysCompletedOperationResult.Instance;
            }

            string compositionContext = @this.Manager.CompositionContext.Name;
            // Return the operation if fake store object already exists.
            if (FakeBackingStore.Exists(compositionContext))
                return FakeBackingStore.Get(compositionContext).InitializeOperation.AsOperationResult();

            FakeBackingStore.Create(compositionContext);

            return ResetFakeBackingStoreAsync(@this);
        }

        /// <summary>Resets the fake backing store to its initial state.</summary>
        public static OperationResult ResetFakeBackingStoreAsync<T>(this IEntityManagerProvider<T> @this)
            where T : EntityManager
        {
            if (!@this.ConnectionOptions.IsFake || !(@this is EntityManagerProvider<T>))
            {
                DebugFns.WriteLine(StringResources.NonSuitableEmpForFakeStoreOperation);
                return AlwaysCompletedOperationResult.Instance;
            }

            return ((EntityManagerProvider<T>)@this).ResetFakeBackingStoreAsync();
        }
    }
}