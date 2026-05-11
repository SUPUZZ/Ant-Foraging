# Ant Foraging

A browser-based **2D ant-foraging simulation** built with HTML5 Canvas and plain JavaScript. Ants leave the nest, search for food, pick it up, and carry it home while depositing **pheromones** on the return leg. Other ants can then bias their movement using those cues, so **short-lived trails** and **collective path use** emerge from simple local rules—without any central planner.

## What this project is

- **Toy model of stigmergy**: ants coordinate indirectly by modifying a shared scalar field (pheromone density on a grid) and reading it back while they move.
- **Interactive sandbox**: reset the scene, place food by clicking the canvas, and tune sliders such as colony size, pheromone strength, decay (evaporation), and exploration-vs-trail bias to see how behavior changes.

## What we explore

- **Emergent trails**: thin, bright “roads” arise when many ants reinforce the same segments on the way back to the nest; they fade when deposition cannot keep up with decay.
- **Exploration vs exploitation**: strong trails speed up finding known food but can discourage detours; weaker signals or faster decay keep the colony more exploratory.
- **Path reinforcement (“strengthen homing”)**: repeating successful return paths increases local pheromone, which visibly steers outbound and searching movement—illustrating how positive feedback loops shape group-level foraging geometry.
- **Parameter sensitivity**: small changes in deposition rate or evaporation can switch the field between “clean almost empty” and “dense maze-like ribbons,” highlighting the coupling between memory (pheromone) and the physical motion rules.

Detailed rules aligned with the code (world size, finite state machine, grid sampling, deposition order, etc.) are documented in Chinese in **`规律.md`**.

## How to run

Open **`ant-foraging.html`** in a modern desktop or mobile browser (no build step).

If you cloned this repo locally, double-click the file or serve the folder with any static file server—the simulation is entirely client-side.

## Repository

Upstream: **https://github.com/SUPUZZ/Ant-Foraging**
