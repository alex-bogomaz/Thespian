namespace Nessos.Thespian.Tests

open System
open NUnit.Framework
open FsUnit

open Nessos.Thespian
open Nessos.Thespian.Tests.TestDefinitions

[<AbstractClass>]
type ``Collocated Communication``() =
  let mutable defaultPrimaryProtocolFactory = Unchecked.defaultof<IPrimaryProtocolFactory>
  
  abstract PrimaryProtocolFactory: IPrimaryProtocolFactory
  default __.PrimaryProtocolFactory = new MailboxPrimaryProtocolFactory() :> IPrimaryProtocolFactory
  abstract PublishActorPrimary: Actor<'T> -> Actor<'T>
  abstract RefPrimary: Actor<'T> -> ActorRef<'T>

  [<TestFixtureSetUp>]
  member self.SetUp() =
    defaultPrimaryProtocolFactory <- Actor.DefaultPrimaryProtocolFactory
    Actor.DefaultPrimaryProtocolFactory <- self.PrimaryProtocolFactory

  [<TestFixtureTearDown>]
  member self.TearDown() =
    Actor.DefaultPrimaryProtocolFactory <- defaultPrimaryProtocolFactory
  
  [<Test>]
  member self.``Post method``() =
    let cell = ref 0
    use actor = Actor.bind <| Behavior.stateless (Behaviors.refCell cell)
                |> self.PublishActorPrimary
                |> Actor.start


    self.RefPrimary(actor).Post(TestAsync 42)
    //do something for a while
    System.Threading.Thread.Sleep(1000)
    cell.Value |> should equal 42

  [<Test>]
  member self.``Post operator``() =
    let cell = ref 0
    use actor = Actor.bind <| Behavior.stateless (Behaviors.refCell cell)
                |> self.PublishActorPrimary
                |> Actor.start

    self.RefPrimary(actor) <-- TestAsync 42
    //do something for a while
    System.Threading.Thread.Sleep(1000)
    cell.Value |> should equal 42

  [<Test>]
  member self.``Post with reply method``() =
    use actor = Actor.bind <| Behavior.stateful 0 Behaviors.state
                |> self.PublishActorPrimary
                |> Actor.start

    self.RefPrimary(actor).Post(TestAsync 42)
    let r = Async.RunSynchronously <| self.RefPrimary(actor).PostWithReply(fun ch -> TestSync(ch, 43))
    r |> should equal 42

  [<Test>]
  member self.``Post with reply operator``() =
    use actor = Actor.bind <| Behavior.stateful 0 Behaviors.state
                |> self.PublishActorPrimary
                |> Actor.start

    self.RefPrimary(actor) <-- TestAsync 42
    let r = self.RefPrimary(actor) <!= fun ch -> TestSync(ch, 43)
    r |> should equal 42

  [<Test>]
  [<ExpectedException(typeof<TimeoutException>)>]
  [<Timeout(60000)>] //make sure the default timeout is less than the test case timeout
  member self.``Post with reply method with no timeout (default timeout)``() =
    use actor = Actor.bind PrimitiveBehaviors.nill |> self.PublishActorPrimary |> Actor.start
    self.RefPrimary(actor) <!= fun ch -> TestSync(ch, ())

  [<Test>]
  member self.``Post with reply method with timeout (in-time)``() =
    use actor = Actor.bind <| Behavior.stateful 0 Behaviors.state
                |> self.PublishActorPrimary
                |> Actor.start

    self.RefPrimary(actor) <-- TestAsync 42
    let r = Async.RunSynchronously <| self.RefPrimary(actor).PostWithReply((fun ch -> TestSync(ch, 43)), 4000)
    r |> should equal 42

  [<Test>]
  [<ExpectedException(typeof<TimeoutException>)>]
  member self.``Post with reply method timeout (fluid) on reply channel overrides method timeout arg``() =
    use actor = Actor.bind <| Behavior.stateful 0 Behaviors.delayedState
                |> self.PublishActorPrimary
                |> Actor.start

    self.RefPrimary(actor) <-- TestAsync 42

    //the actor will stall for Default.ReplyReceiveTimeout,
    //the reply timeout specified by the method arg is Default.ReplyReceiveTimeout * 2
    //enough to get back the reply
    //the timeout is overriden by setting the reply channel timeout to Default.ReplyReceiveTimeout/2
    //thus we expect this to timeout
    self.RefPrimary(actor).PostWithReply((fun ch -> TestSync(ch.WithTimeout(Default.ReplyReceiveTimeout/2), Default.ReplyReceiveTimeout)), Default.ReplyReceiveTimeout * 2)
    |> Async.Ignore
    |> Async.RunSynchronously


[<AbstractClass>]
type ``Collocated Remote Communication``() =
  inherit ``Collocated Communication``()

  [<Test>]
  member self.``Post to collocated actor through a non-collocated ref``() =
    use actor = Actor.bind PrimitiveBehaviors.nill |> self.PublishActorPrimary |> Actor.start

    let serializer = Serialization.defaultSerializer
    let serializedRef = serializer.Serialize(actor.Ref)
    let deserializedRef = serializer.Deserialize<ActorRef<TestMessage<unit, unit>>>(serializedRef)

    deserializedRef <-- TestAsync()

  [<Test>]
  member self.``Post with reply to collocated actor through a non-collocated ref``() =
    use actor = Actor.bind <| Behavior.stateful 0 Behaviors.state
                |> self.PublishActorPrimary
                |> Actor.start

    self.RefPrimary(actor) <-- TestAsync 42

    let serializer = Serialization.defaultSerializer
    let serializedRef = serializer.Serialize(actor.Ref)
    let deserializedRef = serializer.Deserialize<ActorRef<TestMessage<int, int>>>(serializedRef)

    let r = deserializedRef <!= fun ch -> TestSync(ch, 43)
    r |> should equal 42

  [<Test>]
  member self.``Publish to protocol ActorRef.Protocols/ActorRef.ProtocolFactories``() =
    use actor = Actor.bind PrimitiveBehaviors.nill |> self.PublishActorPrimary

    actor.Ref.Protocols.Length |> should equal 2
    actor.Ref.ProtocolFactories.Length |> should equal 1

  [<Test>]
  [<ExpectedException(typeof<UnknownRecipientException>)>]
  member self.``Post to published stopped actor``() =
    use actor = Actor.bind PrimitiveBehaviors.nill |> self.PublishActorPrimary
    self.RefPrimary(actor) <-- TestAsync()

  [<Test>]
  [<ExpectedException(typeof<UnknownRecipientException>)>]
  member self.``Post when started, stop and post``() =
    use actor = Actor.bind <| Behavior.stateful 0 Behaviors.state
                |> self.PublishActorPrimary
                |> Actor.start

    self.RefPrimary(actor) <-- TestAsync 42
    let r = self.RefPrimary(actor) <!= fun ch -> TestSync(ch, 43)
    r |> should equal 42

    actor.Stop()
    self.RefPrimary(actor) <-- TestAsync 0
