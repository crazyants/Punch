﻿//====================================================================================================================
// Copyright (c) 2012 IdeaBlade
//====================================================================================================================
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE 
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS 
// OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR 
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. 
//====================================================================================================================
// USE OF THIS SOFTWARE IS GOVERENED BY THE LICENSING TERMS WHICH CAN BE FOUND AT
// http://cocktail.ideablade.com/licensing
//====================================================================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Caliburn.Micro;
using IdeaBlade.EntityModel;

#if !SILVERLIGHT4
using System.Threading.Tasks;
#endif

namespace Cocktail
{
    /// <summary>
    /// Encapsulates and abstracts a DevForce asynchronous operation.
    /// </summary>
    /// <seealso cref="CoroutineFns"/>
    public class OperationResult : IResult, INotifyCompleted
    {
        private readonly INotifyCompleted _asyncOp;
        private INotifyCompletedArgs _args;

#if !SILVERLIGHT4
        private TaskCompletionSource<bool> _tcs;
#endif

        /// <summary>Constructs a wrapper around the provided asynchronous function.</summary>
        /// <param name="asyncOp">The asynchronous DevForce function to be wrapped.</param>
        public OperationResult(INotifyCompleted asyncOp)
        {
            _asyncOp = asyncOp;
            _asyncOp.WhenCompleted(args => _args = args);
        }

        /// <summary>
        /// Returns whether the operation completed successfully.
        /// </summary>
        public bool CompletedSuccessfully
        {
            get { return _args != null && _args.Error == null; }
        }

        /// <summary>
        /// Returns whether the operation failed. 
        /// </summary>
        public bool HasError
        {
            get { return _args != null && _args.Error != null; }
        }

        /// <summary>
        /// Returns the exception if the operation failed. 
        /// </summary>
        public Exception Error
        {
            get { return _args != null ? _args.Error : null; }
        }

        /// <summary>
        /// Returns whether the error was handled.
        /// </summary>
        public bool IsErrorHandled
        {
            get { return _args != null && _args.IsErrorHandled; }
        }

        /// <summary>
        /// Returns whether the operation was cancelled. 
        /// </summary>
        public bool Cancelled
        {
            get { return _args != null && _args.Cancelled; }
        }

#if !SILVERLIGHT4
        /// <summary>
        /// Returns a Task for the current OperationResult.
        /// </summary>
        public Task AsTask()
        {
            if (_tcs != null) return _tcs.Task;

            _tcs = new TaskCompletionSource<bool>();
            _asyncOp.WhenCompleted(
                args =>
                {
                    if (args.Cancelled)
                        _tcs.TrySetCanceled();
                    else if (args.Error != null && !args.IsErrorHandled)
                    {
                        _tcs.TrySetException(args.Error);
                        args.IsErrorHandled = true;
                    }
                    else
                        _tcs.TrySetResult(true);
                });

            return _tcs.Task;
        }

        /// <summary>
        /// Implicitly converts the current OperationResult to type <see cref="Task"/>
        /// </summary>
        /// <param name="operation">The OperationResult to be converted.</param>
        public static implicit operator Task(OperationResult operation)
        {
            return operation.AsTask();
        }
#endif

        #region Implementation of IResult

        /// <summary>
        /// Executes the result using the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        void IResult.Execute(ActionExecutionContext context)
        {
            _asyncOp.WhenCompleted(OnComplete);
        }

        /// <summary>Signals the completion of the asynchronous operation.</summary>
        event EventHandler<ResultCompletionEventArgs> IResult.Completed
        {
            add { Completed += value; }
            remove { Completed -= value; }
        }

        #endregion

        #region INotifyCompleted Members

        void INotifyCompleted.WhenCompleted(Action<INotifyCompletedArgs> completedAction)
        {
            _asyncOp.WhenCompleted(completedAction);
        }

        #endregion

        private void OnComplete(INotifyCompletedArgs args)
        {
            if (Completed == null) return;

            var resultArgs = new ResultCompletionEventArgs
                                 {
                                     Error = args.IsErrorHandled ? null : args.Error,
                                     WasCancelled = args.Cancelled
                                 };
            EventFns.RaiseOnce(ref Completed, this, resultArgs);
        }

        private event EventHandler<ResultCompletionEventArgs> Completed;

        #region Hide Object Members

        /// <summary>
        /// Hidden.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override bool Equals(object obj)
        {
            // ReSharper disable BaseObjectEqualsIsObjectEquals
            return base.Equals(obj);
            // ReSharper restore BaseObjectEqualsIsObjectEquals
        }

        /// <summary>
        /// Hidden.
        /// </summary>
        // ReSharper disable NonReadonlyFieldInGetHashCode
        [EditorBrowsable(EditorBrowsableState.Never)]
        // ReSharper restore NonReadonlyFieldInGetHashCode
        public override int GetHashCode()
        {
            // ReSharper disable BaseObjectGetHashCodeCallInGetHashCode
            return base.GetHashCode();
            // ReSharper restore BaseObjectGetHashCodeCallInGetHashCode
        }

        /// <summary>
        /// Hidden.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override string ToString()
        {
            return base.ToString();
        }

        /// <summary>
        /// Hidden.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public new Type GetType()
        {
            return base.GetType();
        }

        #endregion
    }

    /// <summary>
    /// Encapsulates and abstracts a DevForce asynchronous operation.
    /// </summary>
    /// <seealso cref="CoroutineFns"/>
    public abstract class OperationResult<T> : OperationResult
    {
#if !SILVERLIGHT4
        private TaskCompletionSource<T> _tcs;
#endif

        /// <summary>Constructs a wrapper around the provided asynchronous function.</summary>
        /// <param name="asyncOp">The asynchronous DevForce function to be wrapped.</param>
        protected OperationResult(INotifyCompleted asyncOp)
            : base(asyncOp)
        {
        }

        /// <summary>
        /// The result value of the operation.
        /// </summary>
        public abstract T Result { get; }

#if !SILVERLIGHT4
        /// <summary>
        /// Returns a Task&lt;T&gt; for the current OperationResult.
        /// </summary>
        public new Task<T> AsTask()
        {
            if (_tcs != null) return _tcs.Task;

            _tcs = new TaskCompletionSource<T>();
            ((INotifyCompleted)this).WhenCompleted(
                args =>
                {
                    if (args.Cancelled)
                        _tcs.TrySetCanceled();
                    else if (args.Error != null && !args.IsErrorHandled)
                    {
                        _tcs.TrySetException(args.Error);
                        args.IsErrorHandled = true;
                    }
                    else
                        _tcs.TrySetResult(args.Error == null ? Result : default(T));
                });

            return _tcs.Task;
        }

        /// <summary>
        /// Implicitly converts the current OperationResult to type <see cref="Task"/>
        /// </summary>
        /// <param name="operation">The OperationResult to be converted.</param>
        public static implicit operator Task(OperationResult<T> operation)
        {
            return operation.AsTask();
        }

        /// <summary>
        /// Implicitly converts the current OperationResult to type <see cref="Task{T}"/>
        /// </summary>
        /// <param name="operation">The OperationResult to be converted.</param>
        public static implicit operator Task<T>(OperationResult<T> operation)
        {
            return operation.AsTask();
        }
#endif
    }

    internal class CoroutineOperationResult<T> : OperationResult<T>
    {
        private readonly CoroutineOperation _coroutineOperation;

        public CoroutineOperationResult(CoroutineOperation coroutineOperation)
            : base(coroutineOperation)
        {
            _coroutineOperation = coroutineOperation;
        }

        public override T Result
        {
            get { return (T)_coroutineOperation.Result; }
        }
    }

    internal class EntityQueryOperationResult : OperationResult<IEnumerable>
    {
        private readonly EntityQueryOperation _entityQueryOperation;

        public EntityQueryOperationResult(EntityQueryOperation entityQueryOperation)
            : base(entityQueryOperation)
        {
            _entityQueryOperation = entityQueryOperation;
        }

        public override IEnumerable Result
        {
            get { return _entityQueryOperation.Results; }
        }
    }

    internal class EntityQueryOperationResult<T> : OperationResult<IEnumerable<T>>
    {
        private readonly EntityQueryOperation _entityQueryOperation;
        private readonly EntityQueryOperation<T> _entityQueryOperationT;

        public EntityQueryOperationResult(EntityQueryOperation<T> entityQueryOperationT)
            : base(entityQueryOperationT)
        {
            _entityQueryOperationT = entityQueryOperationT;
        }

        public EntityQueryOperationResult(EntityQueryOperation entityQueryOperation)
            : base(entityQueryOperation)
        {
            _entityQueryOperation = entityQueryOperation;
        }

        public override IEnumerable<T> Result
        {
            get
            {
                return _entityQueryOperation != null
                           ? _entityQueryOperation.Results.Cast<T>()
                           : _entityQueryOperationT.Results;
            }
        }
    }

    internal class EntityRefetchOperationResult : OperationResult<IEnumerable>
    {
        private readonly EntityRefetchOperation _entityRefetchOperation;

        public EntityRefetchOperationResult(EntityRefetchOperation entityRefetchOperation)
            : base(entityRefetchOperation)
        {
            _entityRefetchOperation = entityRefetchOperation;
        }

        public override IEnumerable Result
        {
            get { return _entityRefetchOperation.Results; }
        }
    }

    internal class EntitySaveOperationResult : OperationResult<SaveResult>
    {
        private readonly EntitySaveOperation _entitySaveOperation;

        public EntitySaveOperationResult(EntitySaveOperation entitySaveOperation)
            : base(entitySaveOperation)
        {
            _entitySaveOperation = entitySaveOperation;
        }

        public override SaveResult Result
        {
            get { return _entitySaveOperation.SaveResult; }
        }
    }

    internal class EntityScalarQueryOperationResult<T> : OperationResult<T>
    {
        private readonly EntityScalarQueryOperation _entityScalarQueryOperation;
        private readonly EntityScalarQueryOperation<T> _entityScalarQueryOperationT;

        public EntityScalarQueryOperationResult(EntityScalarQueryOperation<T> entityScalarQueryOperationT)
            : base(entityScalarQueryOperationT)
        {
            _entityScalarQueryOperationT = entityScalarQueryOperationT;
        }

        public EntityScalarQueryOperationResult(EntityScalarQueryOperation entityScalarQueryOperation)
            : base(entityScalarQueryOperation)
        {
            _entityScalarQueryOperation = entityScalarQueryOperation;
        }

        public override T Result
        {
            get
            {
                return _entityScalarQueryOperation != null
                           ? (T)_entityScalarQueryOperation.Result
                           : _entityScalarQueryOperationT.Result;
            }
        }
    }

    internal class InvokeServerMethodOperationResult<T> : OperationResult<T>
    {
        private readonly InvokeServerMethodOperation _invokeServerMethodOperation;

        public InvokeServerMethodOperationResult(InvokeServerMethodOperation invokeServerMethodOperation)
            : base(invokeServerMethodOperation)
        {
            _invokeServerMethodOperation = invokeServerMethodOperation;
        }

        public override T Result
        {
            get { return (T)_invokeServerMethodOperation.Result; }
        }
    }
}