# APEXlang API Reference

## Components

- [app](#comp-0900)
    - [page](#comp-4000)
    - [restHandler](#comp-3085)
    - [legacyBanner](#comp-0999)

## Components

### `app`

**Direct Properties:**

`name`
`string`
Yes
`Legacy Demo`
maxLen=128

`alias`
`string`
No
none
maxLen=80

`theme`
`string`
No
`Vita`

**Property Groups:**

- navigationMenu

`listPosition`
`enum`
Yes
`top`
enum=[top, side, legacy]

```typescript
app (
  name: "Demo Legacy"
  alias: "DEMO"
  theme: "Vita"
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
No
none

`legacyTemplate`
`string`
No
none

```typescript
page home (
  name: "Home"
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
enum=[GET, POST, TRACE]

### `legacyBanner`

**Direct Properties:**

`title`
`string`
No
none
