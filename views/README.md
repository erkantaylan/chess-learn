# Views

Each file registers one alternative rendering of the move tree via `RT.registerView(...)`.
They are loaded by `index.html` and must not modify it. See the RT API in index.html
(section "view API").

Wherever a view draws something that stands for a move, give it the app's right-click menu
as well as the click: `el.addEventListener("contextmenu", e => RT.moveMenu(e, node))`.
