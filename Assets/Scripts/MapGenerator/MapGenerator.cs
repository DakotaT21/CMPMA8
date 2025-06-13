using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using System.Linq;

public class MapGenerator : MonoBehaviour
{
    public List<Room> rooms;
    public Hallway vertical_hallway;
    public Hallway horizontal_hallway;
    public Room start;
    public Room target;

    // Constraint: How big should the dungeon be at most
    // this will limit the run time (about 10 is a good value 
    // during development, later you'll want to set it to 
    // something a bit higher, like 25-30)
    public int MAX_SIZE = 25;

    // set this to a high value when the generator works
    // for debugging it can be helpful to test with few rooms
    // and, say, a threshold of 100 iterations
    public int THRESHOLD = 1000;

    // keep the instantiated rooms and hallways here 
    private List<GameObject> generated_objects;
    
    int iterations;


    // New fields for bonuses and bookkeeping
    private struct RoomPlacement
    {
        public Room room;
        public Vector2Int offset;
        public Door connectingDoor;
        public RoomPlacement(Room r, Vector2Int o, Door d)
        {
            room = r;
            offset = o;
            connectingDoor = d;
        }
    }
    private List<RoomPlacement> plannedPlacements;
    private int minX, maxX, minY, maxY;
    private int placedTargetCount;
    private Vector2Int targetPosition;

    public int MAX_BOUND_DIFF = 7;    // how many cells wide/tall you allow during placement


    public void Generate()
    {
        // ensure your exit (target) room can actually be placed
        if (!rooms.Contains(target))
            rooms.Add(target);

        // dispose of game objects from previous generation process
        foreach (var go in generated_objects)
        {
            Destroy(go);
        }
        generated_objects.Clear();

        // reset bookkeeping
        plannedPlacements = new List<RoomPlacement>();
        placedTargetCount = 0;
        minX = maxX = 0;
        minY = maxY = 0;

        // place start room at (0,0)
        generated_objects.Add(start.Place(new Vector2Int(0,0)));
        // initialize door list and occupied grid
        List<Door> doors = start.GetDoors();
        List<Vector2Int> occupied = new List<Vector2Int>();
        occupied.Add(new Vector2Int(0, 0));
        iterations = 0;

        // run the backtracker
        bool success = GenerateWithBacktracking(occupied, doors, 1);
        if (!success)
        {
            Debug.LogError("Dungeon generation failed");
            return;
        }

        // instantiate all planned rooms and hallways
        foreach (var placement in plannedPlacements)
        {
            // room
            GameObject roomGO = placement.room.Place(placement.offset);
            generated_objects.Add(roomGO);

            // hallway connecting parent to this room
            if (placement.connectingDoor.IsHorizontal())
                generated_objects.Add(horizontal_hallway.Place(placement.connectingDoor));
            else
                generated_objects.Add(vertical_hallway.Place(placement.connectingDoor));
        }
    }


    bool GenerateWithBacktracking(List<Vector2Int> occupied, List<Door> doors, int depth)
    {
        //if (iterations > THRESHOLD) throw new System.Exception("Iteration limit exceeded");
        //return false;

        // safety valve :contentReference[oaicite:1]{index=1}
        iterations++;
        if (iterations > THRESHOLD)
            throw new System.Exception("Iteration limit exceeded");

        // shuffle doors so different recursion orders get tried
        doors = doors.OrderBy(_ => Random.value).ToList();


        // no more doors, check all constraints :contentReference[oaicite:2]{index=2}
        if (doors.Count == 0)
        {
            // DEBUG: print all of our constraint values
            int pathLen = ComputeShortestPathLength();
            Debug.Log(
                $"[DBG] depth={depth}  placedTarget={placedTargetCount}  " +
                $"boundsX=[{minX},{maxX}]  boundsY=[{minY},{maxY}]  " +
                $"pathLen={pathLen}"
            );

            // Make sure you placed exactly MAX_SIZE rooms
            if (depth != MAX_SIZE) return false;

            // exactly one exit room
            if (placedTargetCount != 1) return false;

            // roughly square shape
            if (Mathf.Abs(maxX - minX) > MAX_BOUND_DIFF || Mathf.Abs(maxY - minY) > MAX_BOUND_DIFF)
                return false;

            // path from start to exit must traverse 5 or more other rooms
            if (pathLen < 6)
                return false;

            return true;
        }


        // pick the next door (first in list)
        Door doorToConnect = doors[0];

        // build a mutable list of candidates for weighted selection
        List<Room> candidates = new List<Room>(rooms);

        // try each candidate exactly once, in weight-guided random order :contentReference[oaicite:3]{index=3}
        while (candidates.Count > 0)
        {
            Room candidate = WeightedPickAndRemove(candidates);

            // Only allow exit as the final room
            bool isExit = (candidate == target);

            // If we are about to place the last room (MAX_SIZE-th), only allow target
            if (depth == MAX_SIZE - 1 && !isExit)
                continue;

            // If we are not at the last room, do not allow placing target
            if (depth < MAX_SIZE - 1 && isExit)
                continue;

            // If depth is already at MAX_SIZE, do not place any more rooms
            if (depth >= MAX_SIZE)
                continue;

            // must have matching door
            if (!candidate.HasDoorOnSide(doorToConnect.GetMatchingDirection()))
                continue;

            // enforce single exit
            if (isExit && placedTargetCount >= 1)
                continue;


            // compute where to place this room
            Vector2Int offset = GetPlacementOffset(doorToConnect, candidate);

            // check for overlap
            bool overlap = false;
            foreach (var cell in candidate.GetGridCoordinates(offset))
                if (occupied.Contains(cell)) { overlap = true; break; }
            if (overlap) continue;

            //// update bounding box and prune shape if already invalid
            int oldMinX = minX, oldMaxX = maxX, oldMinY = minY, oldMaxY = maxY;
            foreach (var cell in candidate.GetGridCoordinates(offset))
            {
                minX = Mathf.Min(minX, cell.x);
                maxX = Mathf.Max(maxX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxY = Mathf.Max(maxY, cell.y);
            }
            if (Mathf.Abs(maxX - minX) > MAX_BOUND_DIFF || Mathf.Abs(maxY - minY) > MAX_BOUND_DIFF)
            {
                minX = oldMinX; maxX = oldMaxX; minY = oldMinY; maxY = oldMaxY;
                continue;
            }

            // mark occupied
            foreach (var cell in candidate.GetGridCoordinates(offset))
                occupied.Add(cell);

            // prepare the new door list
            List<Door> newDoors = new List<Door>(doors);
            newDoors.Remove(doorToConnect);
            foreach (var d in candidate.GetDoors(offset))
                if (!d.IsMatching(doorToConnect))
                    newDoors.Add(d);

            // record exit placement
            if (isExit)
            {
                placedTargetCount++;
                targetPosition = offset;
            }

            // record this placement for later instantiation
            plannedPlacements.Add(new RoomPlacement(candidate, offset, doorToConnect));

            // recurse
            if (GenerateWithBacktracking(occupied, newDoors, depth + 1))
                return true;

            // backtrack
            plannedPlacements.RemoveAt(plannedPlacements.Count - 1);
            if (isExit) placedTargetCount--;
            foreach (var cell in candidate.GetGridCoordinates(offset))
                occupied.Remove(cell);
            //minX = oldMinX; maxX = oldMaxX; minY = oldMinY; maxY = oldMaxY;
        }

        // no candidate succeeded
        return false;


    }


    // Helper: pick one Room from 'candidates' at random weighted by its 'weight' :contentReference[oaicite:4]{index=4}
    Room WeightedPickAndRemove(List<Room> candidates)
    {
        int total = 0;
        foreach (var r in candidates) total += r.weight;

        int pick = Random.Range(0, total);
        int cum = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            cum += candidates[i].weight;
            if (pick < cum)
            {
                Room chosen = candidates[i];
                candidates.RemoveAt(i);
                return chosen;
            }
        }
        // fallback
        var last = candidates[candidates.Count - 1];
        candidates.RemoveAt(candidates.Count - 1);
        return last;
    }

    // Helper: compute how to offset 'candidate' so its matching door aligns to 'doorToConnect'
    Vector2Int GetPlacementOffset(Door doorToConnect, Room candidate)
    {
        Vector2Int targetGrid = doorToConnect.GetGridCoordinates();
        Door.Direction matchDir = doorToConnect.GetMatchingDirection();

        foreach (var candDoor in candidate.GetDoors(new Vector2Int(0, 0)))
        {
            if (candDoor.GetDirection() == matchDir)
            {
                Vector2Int candGrid = candDoor.GetGridCoordinates();
                // align the two
                return targetGrid + DirectionToVector(doorToConnect.GetDirection()) - candGrid;
            }
        }
        return Vector2Int.zero; // should not happen
    }

    // Helper: convert a Door.Direction into a unit offset
    Vector2Int DirectionToVector(Door.Direction d)
    {
        switch (d)
        {
            case Door.Direction.NORTH: return new Vector2Int(0, 1);
            case Door.Direction.SOUTH: return new Vector2Int(0, -1);
            case Door.Direction.EAST: return new Vector2Int(1, 0);
            case Door.Direction.WEST: return new Vector2Int(-1, 0);
        }
        return Vector2Int.zero;
    }


    // Helper: BFS over the placed-room graph to find shortest edge count from start to exit
    private int ComputeShortestPathLength()
    {
        // build adjacency from each placement’s connectingDoor
        var graph = new Dictionary<Vector2Int, List<Vector2Int>>();
        foreach (var rp in plannedPlacements)
        {
            Door d = rp.connectingDoor;
            Vector2Int a = d.GetGridCoordinates();
            Vector2Int b = d.GetMatching().GetGridCoordinates();
            if (!graph.ContainsKey(a)) graph[a] = new List<Vector2Int>();
            if (!graph.ContainsKey(b)) graph[b] = new List<Vector2Int>();
            graph[a].Add(b);
            graph[b].Add(a);
        }

        Vector2Int startCell = new Vector2Int(0, 0);
        Vector2Int exitCell = targetPosition;
        var visited = new HashSet<Vector2Int> { startCell };
        var queue = new Queue<(Vector2Int cell, int dist)>();
        queue.Enqueue((startCell, 0));

        while (queue.Count > 0)
        {
            var (cell, dist) = queue.Dequeue();
            if (cell == exitCell)
                return dist;
            if (!graph.ContainsKey(cell))
                continue;
            foreach (var nbr in graph[cell])
                if (visited.Add(nbr))
                    queue.Enqueue((nbr, dist + 1));
        }

        // no path found
        return int.MaxValue;
    }



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        generated_objects = new List<GameObject>();
        Generate();
    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current.gKey.wasPressedThisFrame)
            Generate();
    }
}
