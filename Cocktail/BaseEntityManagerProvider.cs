//====================================================================================================================
//Copyright (c) 2012 IdeaBlade
//====================================================================================================================
//Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, 
//and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//====================================================================================================================
//The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//the Software.
//====================================================================================================================
//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE 
//WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS 
//OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR 
//OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. 
//====================================================================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Caliburn.Micro;
using IdeaBlade.Core;
using IdeaBlade.EntityModel;
using IdeaBlade.Validation;

namespace Cocktail
{
    /// <summary>Base class for an EntityMangerProvider.</summary>
    /// <typeparam name="T">The type of the EntityManager</typeparam>
    /// <example>
    /// 	<code title="Example" description="Demonstrates how to programatically export the EntityManagerProvider to be used by the application." source="..\..\Workspace\IdeaBlade\IdeaBlade.Application.Framework\Samples\HelloWorld\HelloWorld\EntityManagerProviderFactory.cs" lang="CS"></code>
    /// </example>
    public abstract class BaseEntityManagerProvider<T> : IEntityManagerProvider<T>,
                                                         IHandle<SyncDataMessage<T>>
        where T : EntityManager
    {
        private readonly EventDispatcher<T> _eventDispatcher;
        private IEnumerable<EntityKey> _deletedEntityKeys;
        private IEnumerable<IValidationErrorNotification> _validationErrorNotifiers;
        private IEnumerable<EntityManagerDelegate<T>> _entityManagerInterceptors;

        private readonly PartLocator<IEventAggregator> _eventAggregatorLocator;
        private PartLocator<IAuthenticationService> _authenticationServiceLocator;
        private readonly PartLocator<IEntityManagerSyncInterceptor> _syncInterceptorLocator;

        private T _manager;

        /// <summary>Initializes a new instance.</summary>
        /// <param name="authenticationService">The authentication service to be used. If not provided, the framework will attempt to discover the current authentication service.</param>
        /// <param name="compositionContext">The CompositionContext to be used. If not provided a new default context will be created.</param>
        protected BaseEntityManagerProvider(IAuthenticationService authenticationService = null,
                                            CompositionContext compositionContext = null)
        {
            Context = compositionContext ?? CompositionContext.Default;

            _eventAggregatorLocator =
                new PartLocator<IEventAggregator>(CreationPolicy.Shared, compositionContext: Context);
            _syncInterceptorLocator =
                new PartLocator<IEntityManagerSyncInterceptor>(CreationPolicy.NonShared, true, Context)
                    .WithDefaultGenerator(() => new DefaultEntityManagerSyncInterceptor());

            _eventDispatcher = new EventDispatcher<T>(EntityManagerDelegates);
            _eventDispatcher.PrincipalChanged += OnPrincipalChanged;
            _eventDispatcher.Querying += OnQuerying;
            _eventDispatcher.Saving += OnSaving;
            _eventDispatcher.Saved += OnSaved;

            _authenticationServiceLocator
                = new PartLocator<IAuthenticationService>(CreationPolicy.Shared, true, Context)
                    .WithInitializer(a => _eventDispatcher.InstallEventHandlers(a))
                    .With(authenticationService);
        }

        private IEnumerable<EntityManagerDelegate<T>> EntityManagerDelegates
        {
            get
            {
                if (_entityManagerInterceptors != null) return _entityManagerInterceptors;

                if (!CompositionHelper.IsConfigured) return new EntityManagerDelegate<T>[0];

                _entityManagerInterceptors = Context.GetInstances<EntityManagerDelegate>(CreationPolicy.Any)
                    .OfType<EntityManagerDelegate<T>>()
                    .ToList();

                TraceFns.WriteLine(_entityManagerInterceptors.Any()
                                       ? string.Format(StringResources.ProbedForEntityManagerDelegateAndFoundMatch,
                                                       _entityManagerInterceptors.Count())
                                       : StringResources.ProbedForEntityManagerDelegateAndFoundNoMatch);

                return _entityManagerInterceptors;
            }
        }

        private IEnumerable<IValidationErrorNotification> ValidationErrorNotifiers
        {
            get
            {
                if (_validationErrorNotifiers != null) return _validationErrorNotifiers;

                if (!CompositionHelper.IsConfigured) return new IValidationErrorNotification[0];

                _validationErrorNotifiers =
                    Context.GetInstances<IValidationErrorNotification>(CreationPolicy.Any).ToList();

                TraceFns.WriteLine(_validationErrorNotifiers.Any()
                                       ? string.Format(
                                           StringResources.ProbedForIValidationErrorNotificationAndFoundMatch,
                                           _validationErrorNotifiers.Count())
                                       : StringResources.ProbedForIValidationErrorNotificationAndFoundNoMatch);

                return _validationErrorNotifiers;
            }
        }

        internal void SetAuthenticationServiceForTesting(IAuthenticationService authenticationService)
        {
            _eventDispatcher.InstallEventHandlers(authenticationService);
            _authenticationServiceLocator = new PartLocator<IAuthenticationService>(CreationPolicy.Shared, true)
                .With(authenticationService);
        }

        private IAuthenticationService AuthenticationService
        {
            get { return _authenticationServiceLocator.GetPart(); }
        }

        /// <summary>The IoC container used by the current instance.</summary>
        public CompositionContext Context { get; private set; }

        /// <summary>
        /// Internal use.
        /// </summary>
        private void OnPrincipalChanged(object sender, EventArgs args)
        {
            // Let's clear the cache from the previous user and
            // release the EntityManager. A new EntityManager will
            // automatically be created and linked to the new
            // security context.
            if (_manager != null)
            {
                _manager.Clear();
                _manager.Disconnect();
                _manager.Querying +=
                    delegate { throw new InvalidOperationException(StringResources.InvalidUseOfExpiredEntityManager); };
                _manager.Saving +=
                    delegate { throw new InvalidOperationException(StringResources.InvalidUseOfExpiredEntityManager); };
            }
            _manager = null;
        }

        #region IEntityManagerProvider<T> Members

        /// <summary>Returns the EntityManager managed by this provider.</summary>
        public T Manager
        {
            get
            {
                if (_manager == null)
                {
                    _manager = CreateEntityManagerCore();
                    OnManagerCreated();
                }
                return _manager;
            }
        }

        /// <summary>
        /// Triggers the ManagerCreated event.
        /// </summary>
        protected virtual void OnManagerCreated()
        {
            ManagerCreated(this, EventArgs.Empty);
        }

        /// <summary>
        /// Event fired after the EntityManager got created.
        /// </summary>
        public event EventHandler<EventArgs> ManagerCreated = delegate { };

        /// <summary>Performs the necessary initialization steps for the persistence layer. The specific steps depend on the subtype of EntityManagerProvider used.</summary>
        public virtual INotifyCompleted InitializeAsync()
        {
            return AlwaysCompleted.Instance;
        }

        /// <summary>
        /// Returns true if the last save operation aborted due to a validation error.
        /// </summary>
        public bool HasValidationError { get; private set; }

        /// <summary>
        /// Returns true if a save is in progress. A <see cref="InvalidOperationException"/> is thrown 
        /// if EntityManager.SaveChangesAsync is called while a previous SaveChangesAsync is still in progress.
        /// </summary>
        public bool IsSaving { get; private set; }

#if !SILVERLIGHT
    /// <summary>Performs the necessary initialization steps for the persistence layer. The specific steps depend on the subtype of EntityManagerProvider used.</summary>
        public virtual void Initialize()
        {
        }

#endif

        /// <summary>Indicates whether the persistence layer has been properly initialized.</summary>
        public virtual bool IsInitialized
        {
            get { return true; }
        }

        #endregion

        /// <summary>Internal use.</summary>
        protected virtual T CreateEntityManagerCore()
        {
            if (CompositionHelper.IsRecomposing)
                throw new InvalidOperationException(StringResources.CreatingEntityManagerDuringRecompositionNotAllowed);

            if (CompositionHelper.IsConfigured)
                Context.BuildUp(this);

            T manager = CreateEntityManager();
            _eventDispatcher.InstallEventHandlers(manager);

            LinkAuthentication(manager);

            if (EventAggregator != null)
                EventAggregator.Subscribe(this);

            return manager;
        }

        /// <summary>Internal use.</summary>
        protected bool LinkAuthentication(T manager)
        {
            return _authenticationServiceLocator.IsAvailable && AuthenticationService.LinkAuthentication(manager);
        }

        /// <summary>
        /// Creates the EntityManager to be used for authentication.
        /// </summary>
        protected internal T CreateAuthenticationManager()
        {
            return CreateEntityManager();
        }

        /// <summary>Internal use.</summary>
        private void OnQuerying(object sender, EntityQueryingEventArgs e)
        {
            MustBeInitialized();

            // In design mode all queries must be forced to execute against the cache.
            if (Execute.InDesignMode)
                e.Query = e.Query.With(QueryStrategy.CacheOnly);
        }

        /// <summary>Throws an exception if the EntityManagerProvider is not initialized.</summary>
        /// <exception caption="" cref="System.InvalidOperationException">Thrown if not initialized.</exception>
        protected void MustBeInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException(StringResources.TheEntityManagerProviderHasNotBeenInitialized);
        }

        /// <summary>
        /// Overload to instantiate the concrete type of the EntityManager
        /// </summary>
        /// <returns>T</returns>
        protected abstract T CreateEntityManager();

        #region Data saving and synchronization logic

        private IEventAggregator EventAggregator
        {
            get { return _eventAggregatorLocator.GetPart(); }
        }

        private IEntityManagerSyncInterceptor GetSyncInterceptor()
        {
            IEntityManagerSyncInterceptor syncInterceptor = _syncInterceptorLocator.GetPart();

            // If custom implementation, set EntityManager property
            if (syncInterceptor is EntityManagerSyncInterceptor)
                ((EntityManagerSyncInterceptor)syncInterceptor).EntityManager = Manager;

            return syncInterceptor;
        }

        /// <summary>Internal use.</summary>
        void IHandle<SyncDataMessage<T>>.Handle(SyncDataMessage<T> syncData)
        {
            if (syncData.IsSameProviderAs(this)) return;

            // Merge deletions
            var removers =
                syncData.DeletedEntityKeys.Select(key => Manager.FindEntity(key, false)).Where(
                    entity => entity != null).ToList();
            if (removers.Any()) Manager.RemoveEntities(removers);

            // Merge saved entities
            IEntityManagerSyncInterceptor interceptor = GetSyncInterceptor();
            var mergers = syncData.SavedEntities.Where(interceptor.ShouldImportEntity);
            Manager.ImportEntities(mergers, MergeStrategy.PreserveChangesUpdateOriginal);

            // Signal to our clients that data has changed
            if (syncData.SavedEntities.Any() || syncData.DeletedEntityKeys.Any())
                RaiseDataChangedEvent(syncData.SavedEntities, syncData.DeletedEntityKeys);
        }

        /// <summary>Internal use.</summary>
        private void OnSaved(object sender, EntitySavedEventArgs e)
        {
            try
            {
                if (!e.HasError)
                {
                    IEntityManagerSyncInterceptor interceptor = GetSyncInterceptor();
                    var exportEntities =
                        e.Entities.Where(
                            entity =>
                            interceptor.ShouldExportEntity(entity) &&
                            !_deletedEntityKeys.Contains(EntityAspect.Wrap(entity).EntityKey)).ToList();
                    PublishEntities(exportEntities);
                }
                _deletedEntityKeys = null;
            }
            finally
            {
                IsSaving = false;
            }
        }

        private void PublishEntities(IEnumerable<object> exportEntities)
        {
            if (EventAggregator == null) return;

            var syncData = new SyncDataMessage<T>(this, exportEntities, _deletedEntityKeys);
            EventAggregator.Publish(syncData);

            // Signal to our clients that data has changed
            if (syncData.SavedEntities.Any() || syncData.DeletedEntityKeys.Any())
                RaiseDataChangedEvent(syncData.SavedEntities, syncData.DeletedEntityKeys);
        }

        /// <summary>Internal use.</summary>
        private void OnSaving(object sender, EntitySavingEventArgs e)
        {
            if (IsSaving)
                throw new InvalidOperationException(
                    StringResources.ThisEntityManagerIsCurrentlyBusyWithAPreviousSaveChangeAsync);
            IsSaving = true;

            try
            {
                Validate(e);
                if (e.Cancel)
                {
                    IsSaving = false;
                    return;
                }

                IEntityManagerSyncInterceptor interceptor = GetSyncInterceptor();
                var syncEntities =
                    e.Entities.Where(interceptor.ShouldExportEntity);
                RetainDeletedEntityKeys(syncEntities);
            }
            catch (Exception)
            {
                IsSaving = false;
                throw;
            }
        }

        private void Validate(EntitySavingEventArgs args)
        {
            if (!CompositionHelper.IsConfigured) return;

            var allValidationErrors = new VerifierResultCollection();

            foreach (var entity in args.Entities)
            {
                var entityAspect = EntityAspect.Wrap(entity);
                if (entityAspect.EntityState.IsDeletedOrDetached()) continue;

                var validationErrors = Manager.VerifierEngine.Execute(entity);
                foreach (var i in EntityManagerDelegates)
                    i.Validate(entity, validationErrors);
                // Extract only validation errors
                validationErrors = validationErrors.Errors;

                validationErrors.Where(vr => !entityAspect.ValidationErrors.Contains(vr))
                    .ForEach(entityAspect.ValidationErrors.Add);

                validationErrors.ForEach(allValidationErrors.Add);
            }

            if (allValidationErrors.HasErrors)
            {
                if (!ValidationErrorNotifiers.Any())
                    throw new ValidationException(allValidationErrors.Select(v => v.Message).ToAggregateString("\n"));

                ValidationErrorNotifiers.ForEach(s => s.OnValidationError(allValidationErrors));
                args.Cancel = true;
            }

            HasValidationError = args.Cancel;
        }

        private void RetainDeletedEntityKeys(IEnumerable<object> syncEntities)
        {
            _deletedEntityKeys =
                syncEntities.Where(e => EntityAspect.Wrap(e).EntityState.IsDeleted()).Select(
                    e => EntityAspect.Wrap(e).EntityKey).ToList();
        }

        /// <summary>
        /// Signals that a Save of at least one entity has been performed
        /// or changed entities have been imported from another entity manager.
        /// Clients may use this event to force a data refresh. 
        /// </summary>
        public event EventHandler<DataChangedEventArgs> DataChanged;

        /// <summary>
        /// Internal use.
        /// </summary>
        /// <param name="savedEntities"></param>
        /// <param name="deletedEntityKeys"></param>
        protected void RaiseDataChangedEvent(IEnumerable<object> savedEntities, IEnumerable<EntityKey> deletedEntityKeys)
        {
            List<EntityKey> entityKeys =
                savedEntities.Select(e => EntityAspect.Wrap(e).EntityKey).Concat(deletedEntityKeys).ToList();

            if (!entityKeys.Any()) return;

            var args = new DataChangedEventArgs(entityKeys, Manager);
            if (DataChanged != null) DataChanged(this, args);
        }

        #endregion
    }
}