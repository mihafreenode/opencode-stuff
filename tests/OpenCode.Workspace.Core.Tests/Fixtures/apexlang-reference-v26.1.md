# APEXlang API Reference

## Components

- [app](#comp-1000)
    - [page](#comp-5000)
    - [restHandler](#comp-3085)
    - [deployment](#comp-9000)

## Components

### `app`

**Direct Properties:**

`name`
`string`
Yes
`Demo`
maxLen=255

`alias`
`string`
Yes
none
maxLen=80

**Property Groups:**

- navigationMenu

`listPosition`
`enum`
Yes
`side`
enum=[top, side]

```typescript
app (
  name: "Demo"
  alias: "DEMO"
)
```

### `page`

**Direct Properties:**

`name`
`string`
Yes
none

`alias`
`string`
Yes
none

```typescript
page home (
  name: "Home"
  alias: "HOME"
)
```

### `restHandler`

**Direct Properties:**

`name`
`string`
Yes
none

`method`
`enum`
No
none
enum=[GET, POST]

### `deployment`

**Direct Properties:**

`name`
`string`
Yes
none
