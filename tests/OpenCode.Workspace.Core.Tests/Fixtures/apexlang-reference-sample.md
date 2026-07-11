# APEXlang API Reference

## Components

- [app](#comp-1000)
    - [page](#comp-5000)
        - [region](#comp-5110)
        - [pageItem](#comp-5120)
        - [dynamicAction](#comp-5140)
        - [validation](#comp-5510)
    - [authentication](#comp-3050)
    - [authorization](#comp-3060)
    - [lov](#comp-3530)

## Components

### `app`

**Direct Properties:**

`name`
`string`
Yes
maxLen=255

`alias`
`string`
Yes
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

`alias`
`string`
Yes

```typescript
page home (
  name: "Home"
  alias: "HOME"
)
```

### `region`

**Direct Properties:**

`title`
`string`
Yes

`type`
`string`
Yes

### `pageItem`

**Direct Properties:**

`name`
`string`
Yes

`lov`
`string`
No

### `validation`

**Direct Properties:**

`name`
`string`
No
