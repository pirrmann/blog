---
layout: post
title: "Fable: React you can be proud of!"
tags: Fable, React, Elmish, Performance
date: 2018-02-01
toc: true
---

[Fable](http://fable.io/) when coupled with [Fable.React](https://github.com/fable-compiler/fable-react) and
[Fable.Elmish.React](https://fable-elmish.github.io/react/) are powerful tools to generate javascript applications but
when it come to optimizing the resulting code they can be tricky, especially as the potential pitfalls and possible
optimizations aren't well documented yet.

In this article I'll try to cover these subjects from the point of view of a Fable developer using React as it's primary
method to interact with the DOM, both directly and via `Fable.Elmish.React`. While I'll start with concepts that any
seasoned React developer already know I hope that by the end of the article most of you will have learnt something.

-------------------

Multiple articles :

* React in Fable
* Optimizing React in Fable

TODO:

* Tweaking Should component update
* Using components as delimiters in react diff algorithm
* Use ofList/ofArray
* Concatenation in React vs sprintf
* Capturing lambdas (Avoiding in pure components & class ones)
* Test samples
* Make JSX work on my blog.
* Functional components in other modules

-------------------

Part 1 - React in Fable land
-------------------

-------------------

Starting a sample Fable React project
-------------------------------------

If you want to try the code for yourself you'll need a sample

* Start with the fable template as in the [Getting started guide](http://fable.io/docs/getting-started.html)
* Replace the `h1` and `canvas` tags in `public/index.html` with `<div id="root"></div>`
* Ensure that we have the latest stable version of everything: `.paket\paket.exe update`
* Add `Fable.React` using paket: `.paket/paket.exe add Fable.React -p src/FableApp.fsproj`
* Add the necessary JS libs with `yarn add react react-dom`
* Change the `App.fs` file to look like that:

```fsharp
module FableApp

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.Browser
open Fable.Import.React
open Fable.Helpers.React
open Fable.Helpers.React.Props

let init() =
    let element = str "Hello 🌍"
    ReactDom.render(element, document.getElementById("root"))

init()
```

Creating HTML elements
----------------------

As F# doesn't have any JSX-like transform creating React elements is done as explained in the [React Without JSX](https://reactjs.org/docs/react-without-jsx.html) article, except that instead of directly using `createElement` a
bunch of helpers are available in the
[`Fable.Helpers.React` module](https://github.com/fable-compiler/fable-react/blob/master/src/Fable.React/Fable.Helpers.React.fs).

For HTML elements the resulting syntax is strongly inspired by the [Elm](http://elm-lang.org/) one.

Here is a small sample of the more common ones :

```fsharp
let element =
    // Each HTML element has an helper with the same name
    ul
        // The first parameter is the properties of the elements.
        // For html elements they are specified as a list and for custom
        // elements it's more typical to find a record creation
        [ClassName "my-ul"; Id "unique-ul"]

        // The second parameter is the list of children
        [
            // str is the helper for exposing a string to React as an element
            li [] [ str "Hello 🌍" ]

            // Helpers exists also for other primitive types
            li [] [ str "Answer is: "; ofInt 42 ]
            li [] [ ofFloat 1.42 ]

            // ofOption can be used to return either null or something
            li [] [ ofOption (Some (str "Hello 🌍")) ]
            // And it can also be used to unconditionally return null, rendering nothing
            li [] [ ofOption None ]

            // ofList allow to expose a list to react, as with any list of elements
            // in React each need an unique and stable key
            [1;2;3]
                |> List.map(fun i ->
                    let si = i.ToString()
                    li [Key si] [str si])
                |> ofList

            // fragment is the <Fragment/> element introduced in React 16 to return
            // multiple elements
            [1;2;3]
                |> List.map(fun i ->
                    let si = i.ToString()
                    li [Key si] [str si])
                |> fragment []
        ]
```

React components
----------------------

While it is possible to use react as a templating engine for HTML by using only built-in components what really unlock
the power of React and where lie the biggest potential for optimisation is in it's user-defined components.

Creating Components in F# is really similar to how they are created in modern Javascript. The main difference come when
consuming them as we'll use the `ofType` and `ofFunction` helpers (Instead of using JSX or `React.createElement`).

### Functional Components

The easiest to use F# components are Functional ones, they don't need a class, a simple function taking props and returning a `ReactElement` will do. They can then be created using the `ofType` helper.

Let's see how they are created in javascript:

```jsx
function Welcome(props) {
  return <h1>Hello, {props.name}</h1>;
}

function init() {
    const element = <Welcome name="🌍" />;
    ReactDOM.render(element, document.getElementById("root"));
}
```

And the equivalent in F#:

```fsharp
type [<Pojo>] WelcomeProps = { name: string }

let Welcome { name = name } =
    h1 [] [ str "Hello, "; str name ]

let inline welcome name = ofFunction Welcome { name = name } []

let init() =
    let element = welcome "🌍"
    ReactDom.render(element, document.getElementById("root"))
```

Some notes:
* We had to declare `WelcomeProps` while Javascript could do without, and in addition we had to declare it as `[<Pojo>]`
  to ensure that Fable generate an anonymous JS object instead of creating a class (React reject props passed as class
  instances)
* Using `sprintf` in the F# sample could have seemed natural but using React for it is a lot better on a performance
  standpoint as we'll see later.

*Note: Due to some pecularities of the Fable transform there can be negative performance impact of using them but they are avoidable if you know what to look for.*

<div style="background-color:red;text-align:center">LINK TO PERF EXPLANATION</div>

### Class Components

To create an user-defined component in F# a class must be created that inherit from
`Fable.React.Component<'props,'state>` and implement at least the mandatory `render()` method that returns a
`ReactElement`.

Let's port our "Hello World" Component:

```fsharp
type [<Pojo>] WelcomeProps = { name: string }

type Welcome(initialProps) =
    inherit Component<WelcomeProps, obj>(initialProps)
    override this.render() =
        h1 [] [ str "Hello "; str this.props.name ]

let inline welcome name = ofType<Welcome,_,_> { name = name } []

let init() =
    let element = welcome "🌍"
    ReactDom.render(element, document.getElementById("root"))
```

Nothing special here, the only gotcha is that the props passed in the primary constructor while they are in scope in
the `render()` method should not be used. It can be avoided at the price of a little more complex syntax:

```fsharp
type Welcome =
    inherit Component<WelcomeProps, obj>
    new(props) = { inherit Component<_, _>(props) }
    override this.render() =
        h1 [] [ str "Hello "; str this.props.name ]
```

### Class Component with state

All features of React are available in Fable and while the more "Functionnal" approach of re-rendering with new props
is more natural using mutable state is totally possible :

```fsharp
// A pure, stateless component that will simply display the counter
type [<Pojo>] CounterDisplayProps = { counter: int }

type CounterDisplay(initialProps) =
    inherit PureStatelessComponent<CounterDisplayProps>(initialProps)
    override this.render() =
        div [] [ str "Counter = "; ofInt this.props.counter ]

let inline counterDisplay p = ofType<CounterDisplay,_,_> p []

// Another pure component displaying the buttons
type [<Pojo>] AddRemoveProps = { add: MouseEvent -> unit; remove: MouseEvent -> unit }

type AddRemove(initialProps) =
    inherit PureStatelessComponent<AddRemoveProps>(initialProps)
    override this.render() =
        div [] [
            button [OnClick this.props.add] [str "👍"]
            button [OnClick this.props.remove] [str "👎"]
        ]

let inline addRemove props = ofType<AddRemove,_,_> props []

// The counter itself using state to keep the count
type [<Pojo>] CounterState = { counter: int }

type Counter(initialProps) as this =
    inherit Component<obj, CounterState>(initialProps)
    do
        this.setInitState({ counter = 0})

    // This is the equivalent of doing `this.add = this.add.bind(this)`
    // in javascript (Except for the fact that we can't reuse the name)
    let add = this.Add
    let remove = this.Remove

    member this.Add(_:MouseEvent) =
        this.setState({ counter = this.state.counter + 1 })

    member this.Remove(_:MouseEvent) =
        this.setState({ counter = this.state.counter - 1 })

    override this.render() =
        div [] [
            counterDisplay { CounterDisplayProps.counter = this.state.counter }
            addRemove { add = add; remove = remove }
        ]

let inline counter props = ofType<Counter,_,_> props []

// createEmpty is used to emit '{}' in javascript, an empty object
let init() =
    let element = counter createEmpty
    ReactDom.render(element, document.getElementById("root"))

init()
```

*Note: This sample use a few react-friendly optimizations that will be explained in more details later.*

<div style="background-color:red;text-align:center">LINK TO PERF EXPLANATION</div>

------------------------

[Part 2 - React, how does it work and how to optimize it]
-------------------

------------------------

## How does React works

The mechanism is described in a lot of details on the [Reconciliation](https://reactjs.org/docs/reconciliation.html)
page of React documentation, I won't repeat the details but essentially React Keep a
representation of what it's element tree is currently, and each time a change need to propagate
it will evaluate what the element tree should now be and apply the diff.

A few rules are important for performace:

* Change of element type or component type make React abandon any DOM diffing and the old tree is
  destroyed in favor of the new one.
  * This should be a rare occurence, any big DOM change is pretty slow as it will involve a lot
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

We can roughtly consider for optimization purpose that each component in the tree is in one of theses 4 states after each change (Ordered from better performance-wise to worse) :

1. Not considered (Because it's parent wasn't re-rendered)
1. Returned false to `shouldComponentUpdate`
1. Render was called but returned the same tree as before
1. Render was called but the tree is different and the document DOM need to be mutated

## Specific optimizations

### PureComponent

The first Optimization that is especially useful for us in F# is the PureComponent. It's a component that only update when one of the elements in it's props or state changed and the comparison is done in a shallow way (By comparing references).

It's ideal when everything you manipulate is immutable, you know like F# records 😉.

Let's take a small sample to see how it's good for us:

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
re-render. But as soon as we convert it to a PureComponent it's not updating anymore: None of it's props or state change so react doesn't event call `render()`.

### Beware: Passing functions

If you look in the previous samples, each time I pass a function it's never a lamba declared directly in `render()` or even a member reference but it's a field that point to a member.

The reason for that is that for react to not apply changes for DOM elements or for PureComponent
the references must be the same and lambdas are re-recreated each time so their reference would be different.

But members ? Members stay the same so we should be able to pass `this.Add` and have it work. But
javascript is a weird language where passing `this.Add` would pass the method add without any `this` attached, so to keep the semantic of the F# language Fable helpfully do it for us and transpile it to `this.Add.bind(this)` instead. But this also re-create a reference each time so we must capture the bound version in a variable during the construction of the object.

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

### Using `toArray`/`toList` and refs

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

* Note: We could have kept using `yield!` instead of using `ofList` and it would have worked here
  with only the keys but it's better to always use `ofList`.
  By using it an array is passed to  React and it will warn us on the console if we forget to use `key`.
  It also create a new scope, avoiding problems if we wanted to show another list in the same parent with keys in common, duplicate keys under a same parent aren't supposed to happen.

### To `render()` or not to `render()`

* https://reactjs.org/docs/optimizing-performance.html#avoid-reconciliation
* https://reactjs.org/docs/optimizing-performance.html#shouldcomponentupdate-in-action

### Updating the DOM: The reconciliation

https://reactjs.org/docs/reconciliation.html

## Optimizations in a pure Fable React world

```fsharp
type Props = { counter: int }
type MyCoolComponent(initialProps) =
    inherit PureComponent<Props, obj>(initialProps)

```

Also

```fsharp
open Fable.Core
open Fable.Core.JsInterop

let onClick (f: unit -> unit) = ()

type Foo() as this=
    let add = this.Add
    member this.Add() = ()
    member this.Render() =
        onClick(add)
```
-------------------

## The Flux/Elm pattern, and it's complexities

* https://github.com/facebook/flux/tree/master/examples/flux-concepts

