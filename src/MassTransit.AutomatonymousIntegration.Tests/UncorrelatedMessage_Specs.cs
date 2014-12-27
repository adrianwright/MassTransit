// Copyright 2007-2014 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.AutomatonymousTests
{
    using System;
    using System.Threading.Tasks;
    using Automatonymous;
    using NUnit.Framework;
    using Saga;
    using TestFramework;


    [TestFixture]
    public class When_a_message_is_not_correlated :
        InMemoryTestFixture
    {
        protected override void ConfigureInputQueueEndpoint(IReceiveEndpointConfigurator configurator)
        {
            _machine = new TestStateMachine();
            _repository = new InMemorySagaRepository<Instance>();

            configurator.StateMachineSaga(_machine, _repository, x =>
            {
                x.Correlate(_machine.Started, (i, d) => i.ServiceName == d.ServiceName)
                    .SelectCorrelationId(msg => msg.ServiceId);

                x.Correlate(_machine.CheckStatus, (i, d) => i.ServiceName == d.ServiceName)
                    .RetryLimit(5);
            });
        }

        TestStateMachine _machine;
        InMemorySagaRepository<Instance> _repository;


        class Instance :
            SagaStateMachineInstance
        {
            public Instance(Guid correlationId)
            {
                CorrelationId = correlationId;
            }

            protected Instance()
            {
            }

            public State CurrentState { get; set; }
            public string ServiceName { get; set; }
            public IServiceBus Bus { get; set; }
            public Guid CorrelationId { get; set; }
        }


        class TestStateMachine :
            AutomatonymousStateMachine<Instance>
        {
            public TestStateMachine()
            {
                InstanceState(x => x.CurrentState);

                State(() => Running);
                Event(() => Started);
                Event(() => CheckStatus);

                Initially(
                    When(Started)
                        .Then(context => context.Instance.ServiceName = context.Data.ServiceName)
                        .Respond(context => new StartupComplete {ServiceId = context.Instance.CorrelationId, ServiceName = context.Instance.ServiceName})
                        .TransitionTo(Running));

                During(Running,
                    When(CheckStatus)
                        .Respond(context => new Status("Running", context.Instance.ServiceName)));
            }

            public State Running { get; private set; }
            public Event<Start> Started { get; private set; }
            public Event<CheckStatus> CheckStatus { get; private set; }
        }


        class Status
        {
            public Status(string status, string serviceName)
            {
                StatusDescription = status;
                ServiceName = serviceName;
            }

            public string ServiceName { get; set; }
            public string StatusDescription { get; set; }
        }


        class CheckStatus
        {
            public CheckStatus(string serviceName)
            {
                ServiceName = serviceName;
            }

            public CheckStatus()
            {
            }

            public string ServiceName { get; set; }
        }


        class Start
        {
            public Start(string serviceName, Guid serviceId)
            {
                ServiceName = serviceName;
                ServiceId = serviceId;
            }

            public Start()
            {
            }

            public string ServiceName { get; set; }
            public Guid ServiceId { get; set; }
        }


        class StartupComplete
        {
            public Guid ServiceId { get; set; }
            public string ServiceName { get; set; }
        }


//        [Test]
//        public async void Should_create_the_saga_with_any_id()
//        {
//            Guid serviceId = NewId.NextGuid();
//
//            Bus.PublishRequest(new Start("A", serviceId), x =>
//            {
//                x.Handle<StartupComplete>(responseReceived.Set);
//                x.HandleTimeout(8.Seconds(), () =>
//                {
//                });
//            });
//
//            Assert.IsTrue(responseReceived.IsAvailable(0.Seconds()));
//            Assert.AreEqual("A", responseReceived.Message.ServiceName);
//
//            Assert.AreEqual(serviceId, responseReceived.Message.ServiceId);
//
//            Assert.IsNotNull(_repository.ShouldContainSaga(responseReceived.Message.ServiceId, 1.Seconds()));
//        }

        [Test]
        public async void Should_retry_the_status_message()
        {
            Task<Status> statusTask = null;
            Request<CheckStatus> request = await Bus.Request(InputQueueAddress, new CheckStatus("A"), x =>
            {
                statusTask = x.Handle<Status>();
                x.Timeout = TestTimeout;
            }, TestCancellationToken);

            await Bus.Publish(new Start("A", Guid.NewGuid()));

            Status status = await statusTask;

            Assert.AreEqual("A", status.ServiceName);
        }
    }
}