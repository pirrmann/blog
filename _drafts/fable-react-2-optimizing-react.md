---
layout: post
title: "Fable: React you can be proud of ! Part 2: Optimizing React"
tags: Fable, React, Elmish, Performance
date: 2018-02-06
---

TODO:

* Tweaking Should component update
* Concatenation in React vs sprintf
* Test samples
* Functional components in other modules

To optimize React we'll need to dig into how it works and I'll show a series of specific
optimizations to use.

The post is part of a series on Fable / React optimizations split in 3 parts:

1. [React in Fable land](fable-react-1-react-in-fable-land.html)
1. [Optimizing React](fable-react-2-optimizing-react.html) (This one)
1. Applying to Elmish

## How does React works

The mechanism is described in a lot of details on the [Reconciliation](https://reactjs.org/docs/reconciliation.html)
page of React documentation, I won't repeat the details but essentially React Keep a
representation of what it's element tree is currently, and each time a change need to propagate
it will evaluate what the element tree should now be and apply the diff.

A few rules are important for performance:

* Change of element type or component type make React abandon any DOM diffing and the old tree is
  destroyed in favor of the new one.
  * This should be a rare occurrence, any big DOM change is pretty slow as it will involve a lot
    of destruction / creation by the browser and a lot of reflowing.
  * On the other hand we want this to happen if we know that the HTML elements under some
    Component will drastically change: no need to force React to diff 100s of elements if we know
    that it's a very different page that is shown. The diff also has a price.
* React mostly compare elements in order so adding an element at the start of a parent will
  change ALL children. The `key` attribute should be used to override this behavior for anything
  that is more or less a list of elements.
  * Keys should also be stable, an array index for example is a pretty bad key as it's nearly
    what React does when there are no keys.
* The fastest way to render a component is to not render it at all, so `shouldComponentUpdate`
  (And `PureComponent` that is using it) are the best tools for us.
* Functional components are always rendered, but they are cheaper than normal ones as all the
  lifetime methods are bypassed too.

We can roughly consider for optimization purpose that each component in the tree is in one of theses 4 states after each change (Ordered from better performance-wise to worse) :

1. Not considered (Because it's parent wasn't re-rendered) ❄️❄️
1. Returned false to `shouldComponentUpdate` ❄️
1. Render was called but returned the same tree as before 🔥
1. Render was called but the tree is different and the document DOM need to be mutated 🔥🔥

## PureComponent

The first Optimization that is especially useful for us in F# is the PureComponent. It's a component that only update when one of the elements in it's props or state changed and the comparison is done in a shallow way (By comparing references).

It's ideal when everything you manipulate is immutable, you know like F# records 😉.

Let's take a small sample to see how it's good for us. Test it as-is and with the `Component` replaced with a `PureComponent` :

```fsharp
type Canary(initialProps) =
    inherit Component<obj, obj>(initialProps) // <-- Change to PureComponent here
    let mutable x = 0
    override this.render() =
        x <- x + 1
        div [] [ofInt x; str " = "; str (if x > 1 then "☠️" else "🐤️") ]

let inline canary () = ofType<Canary,_,_> createEmpty []

type [<Pojo>] CounterState = { counter: int }

type Counter(initialProps) as this =
    inherit Component<obj, CounterState>(initialProps)
    do
        this.setInitState({ counter = 0})

    let add = this.Add

    member this.Add(_:MouseEvent) =
        this.setState({ counter = this.state.counter + 1 })

    override this.render() =
        div [] [
            canary ()
            div [] [ str "Counter = "; ofInt this.state.counter ]
            button [OnClick add] [str "👍"]
        ]

let inline counter () = ofType<Counter,_,_> createEmpty []

let init() =
    ReactDom.render(counter (), document.getElementById("root"))
```

While our canary has no reason to update, each time the button is clicked it will actually
re-render. But as soon as we convert it to a `PureComponent` it's not updating anymore: None of it's props or state change so react doesn't event call `render()`.

## Beware: Passing functions

If you look in the previous samples, each time I pass a function it's never a lambda declared directly in `render()` or even a member reference but it's a field that point to a member.

The reason for that is that for react to not apply changes for DOM elements or for PureComponent
the references must be the same and lambdas are re-recreated each time so their reference would be different.

But members ? Members stay the same so we should be able to pass `this.Add` and have it work. But
JavaScript is a weird language where passing `this.Add` would pass the method add without any `this` attached, so to keep the semantic of the F# language Fable helpfully do it for us and transpile it to `this.Add.bind(this)` instead. But this also re-create a reference each time so we must capture the bound version in a variable during the construction of the object.

It's hard to prove it with the `button` so let's prove it by moving the button creation to our lovely 🐤 :

```fsharp
type [<Pojo>] CanaryProps = { add: MouseEvent -> unit }

type Canary(initialProps) =
    inherit PureComponent<CanaryProps, obj>(initialProps)
    let mutable x = 0
    override this.render() =
        x <- x + 1
        div [] [
            button [OnClick this.props.add] [str "👍"]
            span [] [ofInt x; str " = "; str (if x > 1 then "☠️" else "️️️🐤️") ]
        ]

let inline canary props = ofType<Canary,_,_> props []

type [<Pojo>] CounterState = { counter: int }

type Counter(initialProps) as this =
    inherit Component<obj, CounterState>(initialProps)
    do
        this.setInitState({ counter = 0})

    let add = this.Add

    member this.Add(_:MouseEvent) =
        this.setState({ counter = this.state.counter + 1 })

    override this.render() =
        div [] [
            canary { add = add }
            canary { add = this.Add }
            canary { add = (fun _ -> this.setState({ counter = this.state.counter + 1 })) }
            div [] [ str "Counter = "; ofInt this.state.counter ]
        ]

let inline counter () = ofType<Counter,_,_> createEmpty []

let init() =
    ReactDom.render(counter (), document.getElementById("root"))
```

## Using `toArray`/`toList` and refs

It's tempting in F# to use list expressions to build React children even when we have lists as it
allow for a very nice syntax, but it can be a performance problem and force useless renders, let's see a problematic sample :

```fsharp
type [<Pojo>] CanaryProps = { name: string }

type Canary(initialProps) =
    inherit PureComponent<CanaryProps, obj>(initialProps) // <-- Change to PureComponent here
    let mutable x = 0
    override this.render() =
        x <- x + 1
        div [] [str (if x > 1 then "☠️" else "️️️🐤️"); str " "; str this.props.name ]

let inline canary props = ofType<Canary,_,_> props []

let goodNames = ["Chantilly"; "Pepe"; "Lester"; "Pete"; "Baby"; "Sunny"; "Bluebird"]

type [<Pojo>] CanariesState = { i: int; names: string list }

type Counter(initialProps) as this =
    inherit Component<obj, CanariesState>(initialProps)
    do
        this.setInitState({ i = 0; names = [] })

    let add = this.Add

    member this.Add(_:MouseEvent) =
        let name = goodNames.[this.state.i % goodNames.Length]
        let names = name :: this.state.names
        this.setState({ i = this.state.i + 1; names = names })

    override this.render() =
        div [] [
            yield button [OnClick add] [str "🥚"]
            yield! this.state.names |> List.map(fun n -> canary { name = n })
        ]

let inline counter () = ofType<Counter,_,_> createEmpty []

let init() =
    ReactDom.render(counter (), document.getElementById("root"))
```

It seem that *Chantilly* survives but in fact it's an illusion, a new element is always created at the end with his name, and all others are mutated.

So let's fix it by assigning an unique key to all our canaries :

```fsharp
type [<Pojo>] CanaryProps = { key: string; name: string }

type Canary(initialProps) =
    inherit PureComponent<CanaryProps, obj>(initialProps) // <-- Change to PureComponent here
    let mutable x = 0
    override this.render() =
        x <- x + 1
        div [] [str (if x > 1 then "☠️" else "️️️🐤️"); str " "; str this.props.name ]

let inline canary props = ofType<Canary,_,_> props []

let goodNames = ["Chantilly"; "Pepe"; "Lester"; "Pete"; "Baby"; "Sunny"; "Bluebird"]

type [<Pojo>] CanariesState = { i: int; canaries: (int*string) list }

type Counter(initialProps) as this =
    inherit Component<obj, CanariesState>(initialProps)
    do
        this.setInitState({ i = 0; canaries = [] })

    let add = this.Add

    member this.Add(_:MouseEvent) =
        let name = goodNames.[this.state.i % goodNames.Length]
        let canaries = (this.state.i,name) :: this.state.canaries
        this.setState({ i = this.state.i + 1; canaries = canaries })

    override this.render() =
        div [] [
            button [OnClick add] [str "🥚"]
            this.state.canaries
                |> List.map(fun (i,n) -> canary { key = i.ToString(); name = n })
                |> ofList
        ]

let inline counter () = ofType<Counter,_,_> createEmpty []

let init() =
    ReactDom.render(counter (), document.getElementById("root"))
```

We could have kept using `yield!` instead of using `ofList` and it would have worked here
with only the keys but it's better to always use `ofList`.*

By using it an array is passed to  React and it will warn us on the console if we forget to use
`key`.

It also create a new scope, avoiding problems if we wanted to show another list in the same
parent with keys in common, duplicate keys under a same parent aren't supposed to happen.