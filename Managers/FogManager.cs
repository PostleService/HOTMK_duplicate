using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering.Universal;

public class FogManager : MonoBehaviour
{
    private GameObject _player;
    private GameObject _lvl3;
    private GridLayout _gridLayout;

    public Tilemap FogTilemap;
    public Tile[] TileArray;
    [Tooltip("For debugging preferrably. Otherwise - set reconceal tiles to false from start")]
    public bool SpawnFogFromStart = true;
    public bool ReconcealTiles = false;
    [Tooltip("At what distance player's entity removes fog")]
    public int DistanceFromPlayer;
    [Tooltip("At what distance lvl3's entity creates fog")]
    public int DistanceFromLvl3;
    private int _createEdgeAtDistance = 1; // what int to add to revealed area to recalculate edges
    [Tooltip("Dim player light at distance from lvl3 enemy")]
    public float DimLightAtDistance = 6f;
    [Tooltip("Should not be lower than global light not to create 'negative' light")]
    public float LowestLightValue = 0.5f;
    
    private float _playerLightStartIntensity;
    private float _globalLightStartIntensity;

    [Tooltip("Objects apart from player that will reveal fog of war")]
    [HideInInspector] public List<GameObject> ListOfDeconcealingObjects = new List<GameObject>();

    // These structs monitor player and lvl3 positions
    public Vector2Int PlayerPosition = Vector2Int.zero;
    private Vector2Int _playerPosition
    {
        set
        {
            if (PlayerPosition != value)
            {
                if (_player != null)
                {
                    PlayerPosition = value;
                    Reconceal();
                    Reveal();
                    CreateRevealEdges();
                }
            }
        }
    }

    public Vector2Int Lvl3Position = Vector2Int.zero;
    private Vector2Int _lvl3Position
    {
        set
        {
            if (Lvl3Position != value)
            {
                Lvl3Position = value;
                Reconceal();
                if (_lvl3 != null) 
                { 
                    Conceal();
                    CreateConcealEdges();
                }
                if (_player != null)
                {
                    Reveal();
                    CreateRevealEdges();
                }
            }
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        _player = GameObject.Find("PlayerCamera"); // reveal a part of the map before the player spawns

        _globalLightStartIntensity = GameObject.Find("GlobalLight").GetComponent<Light2D>().intensity;

        _gridLayout = FogTilemap.GetComponentInParent<GridLayout>();
        if (SpawnFogFromStart) { SpawnStartingFog(); }
    }

    // Update is called once per frame
    void FixedUpdate()
    { 
        FindPlayerAndLvl3Pos();
        DimPlayerLight();
    }

    private void OnEnable()
    { 
        LevelManagerScript.OnRememberFog += StopConcealingFog;
        EnemyScript.OnDie += DespawnAllFog;
        PlayerScript.OnSpawn += AssignPlayer;
    }

    private void OnDisable()
    { 
        LevelManagerScript.OnRememberFog -= StopConcealingFog;
        EnemyScript.OnDie -= DespawnAllFog;
        PlayerScript.OnSpawn -= AssignPlayer;
    }

    private void AssignPlayer()
    {
        _player = GameObject.Find("Player");
        _playerLightStartIntensity = _player.GetComponent<Light2D>().intensity;
    }

    // After getting starting values of light global and light for player at Start()
    // Check if distance is smaller than DimLightAtDistance
    // If so - calculate how much smaller it is and apply coefficient to player light intensity
    // but set player light intensity no lower than global
    private void DimPlayerLight()
    {
        if (_player != null)
        {
            if (GameObject.Find("LevelManager").GetComponent<LevelManagerScript>().LevelStage == 3 && _lvl3 != null)
            {
                if (Vector2.Distance(PlayerPosition, Lvl3Position) < DimLightAtDistance)
                {
                    float LowestLightIntensity;
                    if (_globalLightStartIntensity > LowestLightValue) { LowestLightIntensity = _globalLightStartIntensity; }
                    else { LowestLightIntensity = LowestLightValue; }

                    float newLightIntensity;
                    float coefficient = Vector2.Distance(PlayerPosition, Lvl3Position) / DimLightAtDistance;
                    float tempLightIntensity = _playerLightStartIntensity * coefficient;
                    if (tempLightIntensity >= LowestLightIntensity)
                    { newLightIntensity = tempLightIntensity; }
                    else { newLightIntensity = LowestLightIntensity; }
                    _player.GetComponent<Light2D>().intensity = newLightIntensity;
                }
            }
            // Relight player after Lvl3 enemy is dead
            else if (GameObject.Find("LevelManager").GetComponent<LevelManagerScript>().LevelStage >= 3)
            {
                if (_player.GetComponent<PlayerScript>() != null)
                {
                    if (_player.GetComponent<Light2D>().intensity < _playerLightStartIntensity)
                    { _player.GetComponent<Light2D>().intensity += Time.deltaTime; }
                    else if (_player.GetComponent<Light2D>().intensity > _playerLightStartIntensity)
                    { _player.GetComponent<Light2D>().intensity = _playerLightStartIntensity; }
                }
            }
        }
    }

    // FindPlayerPosition just keeps track of the player position changes
    // The struct _player position calls functions to update fog if change is detected
    private void FindPlayerAndLvl3Pos()
    {
        Vector2 playerPosition = Vector2.zero;
        Vector2 lvl3Position = Vector2.zero;

        if (_player != null) { playerPosition = _player.transform.position; }
        _playerPosition = Vector2Int.RoundToInt((playerPosition));

        if (_lvl3 != null) { lvl3Position = _lvl3.transform.position; }
        _lvl3Position = Vector2Int.RoundToInt((lvl3Position));

        // will find _lvl3 enemy once LevelStage is 3
        if (GameObject.Find("LevelManager").GetComponent<LevelManagerScript>().LevelStage == 3 && _lvl3 == null) 
        {
            GameObject[] goList = FindObjectsOfType<GameObject>();
            foreach (GameObject go in goList)
            { 
                if (go.GetComponent<EnemyScript>() != null && go.GetComponent<EnemyScript>().ItemStageLevel == 3)
                { _lvl3 = go; }
            }
        }
    }

    // PLAYER AREA REVEAL
    private void Reveal()
    {
        Dictionary<GameObject, Vector3Int> GoVec3 = new Dictionary<GameObject, Vector3Int>();
        GoVec3.Add(_player, new Vector3Int(PlayerPosition.x, PlayerPosition.y, 0));
        foreach (GameObject go in ListOfDeconcealingObjects)
        {
            Vector3Int goVec3Int = Vector3Int.RoundToInt(go.transform.position);
            GoVec3.Add(go, goVec3Int);            
        }

        // for x, for y, from (distance from player * -1) to distance from player
        // check whether vector in bounds and HasTile, create a list of only vectors that contain tiles
        foreach (KeyValuePair<GameObject,Vector3Int> kvp in GoVec3)
        {
            int FogClearDist = kvp.Key.GetComponent<Visibility_Observer>().FogClearDistance;
            Vector3Int ObjPos = kvp.Value;

            List<Vector3Int> cellPosList = new List<Vector3Int>();
            Vector3Int[] cellPosArray = new Vector3Int[] { };
            List<TileBase> tbList = new List<TileBase>();
            TileBase[] tbArray = new TileBase[] { };

            for (int x = (FogClearDist * -1); x <= FogClearDist; x++)
            {
                for (int y = (FogClearDist * -1); y <= FogClearDist; y++)
                {
                    int posX = ObjPos.x + x;
                    int posY = ObjPos.y + y;

                    float distance = Vector3Int.Distance(ObjPos, new Vector3Int(posX, posY)); // checking distance from player here so that the area is somewhat representative of a circle rather than a square
                    Vector3Int cellPos = _gridLayout.WorldToCell(new Vector3Int(posX, posY, 0));

                    if (FogTilemap.HasTile(cellPos) && distance <= FogClearDist)
                    { cellPosList.Add(cellPos); tbList.Add(null); } // have to write elements to list no to forget them and create actual tilebase objects, even if they are null
                }
            }
            cellPosArray = cellPosList.ToArray();
            tbArray = tbList.ToArray(); // SetTiles function is less resource heavy than SetTile one by one, thus creating arrays for SetTiles in this step

            FogTilemap.SetTiles(cellPosArray, tbArray);
        }
    }

    private void CreateRevealEdges()
    {
        Dictionary<GameObject, Vector3Int> GoVec3 = new Dictionary<GameObject, Vector3Int>();
        GoVec3.Add(_player, new Vector3Int(PlayerPosition.x, PlayerPosition.y, 0));
        foreach (GameObject go in ListOfDeconcealingObjects)
        {
            Vector3Int goVec3Int = Vector3Int.RoundToInt(go.transform.position);
            GoVec3.Add(go, goVec3Int);
        }

        // for x, for y, from (distance from player * -1) to distance from player
        // check whether vector in bounds and HasTile, create a list of only vectors that contain tiles
        foreach (KeyValuePair<GameObject, Vector3Int> kvp in GoVec3)
        {
            int FogClearDist = kvp.Key.GetComponent<Visibility_Observer>().FogClearDistance;
            Vector3Int ObjPos = kvp.Value;
            List<Vector3Int> cellPosList = new List<Vector3Int>();

            // calculate tiles 1 unit further than the tiles are set to disappear
            for (int x = ((FogClearDist + _createEdgeAtDistance) * -1); x <= (FogClearDist + _createEdgeAtDistance); x++)
            {
                for (int y = ((FogClearDist + _createEdgeAtDistance) * -1); y <= (FogClearDist + _createEdgeAtDistance); y++)
                {
                    int posX = ObjPos.x + x;
                    int posY = ObjPos.y + y;
                    Vector3Int cellPos = _gridLayout.WorldToCell(new Vector3Int(posX, posY, 0));

                    if (FogTilemap.HasTile(cellPos))
                    { cellPosList.Add(cellPos); } // have to write elements to list no to forget them and create actual tilebase objects, even if they are null
                }
            }
            foreach (Vector3Int vec3 in cellPosList)
            {
                int mask = FogTilemap.HasTile(vec3 + new Vector3Int(0, 1, 0)) ? 1 : 0;
                mask += FogTilemap.HasTile(vec3 + new Vector3Int(1, 1, 0)) ? 2 : 0;
                mask += FogTilemap.HasTile(vec3 + new Vector3Int(1, 0, 0)) ? 4 : 0;
                mask += FogTilemap.HasTile(vec3 + new Vector3Int(1, -1, 0)) ? 8 : 0;
                mask += FogTilemap.HasTile(vec3 + new Vector3Int(0, -1, 0)) ? 16 : 0;
                mask += FogTilemap.HasTile(vec3 + new Vector3Int(-1, -1, 0)) ? 32 : 0;
                mask += FogTilemap.HasTile(vec3 + new Vector3Int(-1, 0, 0)) ? 64 : 0;
                mask += FogTilemap.HasTile(vec3 + new Vector3Int(-1, 1, 0)) ? 128 : 0;
                int index = GetIndex((byte)mask);

                if (index >= 0 && index < TileArray.Length)
                { FogTilemap.SetTile(vec3, TileArray[index]); }
                // any tiles that do not correspond to the pattern get substituted with a fully concealed tile:
                // else FogTilemap.SetTile(vec3, TileArray[0]); 
            }

        }
    }

    private void Reconceal()
    {
        if (ReconcealTiles)
        {
            Vector3Int playerPos = new Vector3Int(PlayerPosition.x, PlayerPosition.y, 0);
            List<Vector3Int> cellPosList = new List<Vector3Int>();
            Vector3Int[] cellPosArray = new Vector3Int[] { };
            List<TileBase> tbList = new List<TileBase>();
            TileBase[] tbArray = new TileBase[] { };

            int Distance = _player.GetComponent<Visibility_Observer>().FogClearDistance;

            for (int x = ((Distance + _createEdgeAtDistance + 1) * -1); x <= (Distance + _createEdgeAtDistance + 1); x++)
            {
                for (int y = ((Distance + _createEdgeAtDistance + 1) * -1); y <= (Distance + _createEdgeAtDistance + 1); y++)
                {
                    int posX = playerPos.x + x;
                    int posY = playerPos.y + y;

                    float distance = Vector3Int.Distance(playerPos, new Vector3Int(posX, posY)); // checking distance from player here so that the area is somewhat representative of a circle rather than a square
                    Vector3Int cellPos = _gridLayout.WorldToCell(new Vector3Int(posX, posY, 0));

                    if (distance >= (Distance + _createEdgeAtDistance))
                    {
                        cellPosList.Add(cellPos);
                        tbList.Add(TileArray[0]);
                    } // have to write elements to list no to forget them and create actual tilebase objects, even if they are null
                }
            }
            cellPosArray = cellPosList.ToArray();
            tbArray = tbList.ToArray(); // SetTiles function is less resource heavy than SetTile one by one, thus creating arrays for SetTiles in this step

            FogTilemap.SetTiles(cellPosArray, tbArray);
        }
    }

    // LVL3 AREA CONCEAL
    private void Conceal()
    {
        // for x, for y, from (distance from player * -1) to distance from player
        // check whether vector in bounds and HasTile, create a list of only vectors that contain tiles
        Vector3Int lvl3Pos = new Vector3Int(Lvl3Position.x, Lvl3Position.y, 0);
        List<Vector3Int> cellPosList = new List<Vector3Int>();
        Vector3Int[] cellPosArray = new Vector3Int[] { };
        List<TileBase> tbList = new List<TileBase>();
        TileBase[] tbArray = new TileBase[] { };

        for (int x = (DistanceFromLvl3 * -1); x <= DistanceFromLvl3; x++)
        {
            for (int y = (DistanceFromLvl3 * -1); y <= DistanceFromLvl3; y++)
            {
                int posX = lvl3Pos.x + x;
                int posY = lvl3Pos.y + y;

                float distance = Vector3Int.Distance(lvl3Pos, new Vector3Int(posX, posY)); // checking distance from lvl3 here so that the area is somewhat representative of a circle rather than a square
                Vector3Int cellPos = _gridLayout.WorldToCell(new Vector3Int(posX, posY, 0));

                if (distance <= DistanceFromLvl3)
                { cellPosList.Add(cellPos); tbList.Add(TileArray[0]); } // have to write elements to list no to forget them and create actual tilebase objects, even if they are null
            }
        }
        cellPosArray = cellPosList.ToArray();
        tbArray = tbList.ToArray(); // SetTiles function is less resource heavy than SetTile one by one, thus creating arrays for SetTiles in this step

        FogTilemap.SetTiles(cellPosArray, tbArray);
    }

    private void CreateConcealEdges()
    {
        Vector3Int lvl3Pos = new Vector3Int(Lvl3Position.x, Lvl3Position.y, 0);
        List<Vector3Int> cellPosList = new List<Vector3Int>();

        // calculate same distance from lvl3 as conceal to create edges on top of freshly concealed tiles
        for (int x = ((DistanceFromLvl3) * -1); x <= (DistanceFromLvl3); x++)
        {
            for (int y = ((DistanceFromLvl3) * -1); y <= (DistanceFromLvl3); y++)
            {
                int posX = lvl3Pos.x + x;
                int posY = lvl3Pos.y + y;
                Vector3Int cellPos = _gridLayout.WorldToCell(new Vector3Int(posX, posY, 0));

                if (FogTilemap.HasTile(cellPos))
                { cellPosList.Add(cellPos); } // have to write elements to list no to forget them and create actual tilebase objects, even if they are null
            }
        }
        // REWORKING THIS NOW
        foreach (Vector3Int vec3 in cellPosList)
        {
            int mask = FogTilemap.HasTile(vec3 + new Vector3Int(0, 1, 0)) ? 1 : 0;
            mask += FogTilemap.HasTile(vec3 + new Vector3Int(1, 1, 0)) ? 2 : 0;
            mask += FogTilemap.HasTile(vec3 + new Vector3Int(1, 0, 0)) ? 4 : 0;
            mask += FogTilemap.HasTile(vec3 + new Vector3Int(1, -1, 0)) ? 8 : 0;
            mask += FogTilemap.HasTile(vec3 + new Vector3Int(0, -1, 0)) ? 16 : 0;
            mask += FogTilemap.HasTile(vec3 + new Vector3Int(-1, -1, 0)) ? 32 : 0;
            mask += FogTilemap.HasTile(vec3 + new Vector3Int(-1, 0, 0)) ? 64 : 0;
            mask += FogTilemap.HasTile(vec3 + new Vector3Int(-1, 1, 0)) ? 128 : 0;
            int index = GetIndex((byte)mask);

            if (index >= 0 && index < TileArray.Length)
            { FogTilemap.SetTile(vec3, TileArray[index]); }
            // any tiles that do not correspond to the pattern get substituted with a fully concealed tile:
            // else FogTilemap.SetTile(vec3, TileArray[0]); 
        }
    }

    private int GetIndex(byte mask)
    {
        switch (mask)
        {
            case 255: return 0;
            case 124: return 1;
            case 241: return 2;
            case 199: return 3;
            case 31: return 4;
            case 60: return 9;
            case 30: return 9;
            case 120: return 10;
            case 240: return 10;
            case 225: return 11;
            case 195: return 11;
            case 135: return 12;
            case 15: return 12;
            
            // 3/4 pieces which we can decide upon later. or come up with better calculations    
            // case 127: return 5;
            // case 253: return 6;
            // case 247: return 7;
            // case 223: return 8;
        }
        return -1;
    }

    private void SpawnStartingFog()
    {
        Tilemap[] tilemapsArray = GameObject.FindObjectsOfType<Tilemap>();
        Tilemap tilemap = null;
        List<Vector3Int> AllTiles = new List<Vector3Int>();
        Vector3Int[] cellPosArray = new Vector3Int[] { };
        List<TileBase> tbList = new List<TileBase>();
        TileBase[] tbArray = new TileBase[] { };
        foreach (Tilemap tlmp in tilemapsArray)
        {
            if (tlmp.name == "Abisso")
            { tilemap = tlmp; }
        }

        // if I get lost for any reason, original grid cellcize was 1 with tiles = 32x32 pixels
        Grid grid = FindObjectOfType<Grid>();
        float scaleX = grid.cellSize.x; float scaleY = grid.cellSize.y;

        Bounds tlmpBounds = tilemap.GetComponent<TilemapRenderer>().bounds;
        float boundsXmin = tlmpBounds.min.x; float boundsXmax = tlmpBounds.max.x;
        float boundsYmin = tlmpBounds.min.y; float boundsYmax = tlmpBounds.max.y;

        // We are not adding absolute values, because tile center is in 0.5,0.5 coordinates for tile of scale 1.
        // Hence the formula of dividing by 2
        for (float x = (boundsXmin + (scaleX / 2)); x <= (boundsXmax - (scaleX / 2)); x += scaleX)
        { for (float y = (boundsYmin + (scaleY / 2)); y <= (boundsYmax - (scaleY / 2)); y += scaleY)
            {
                Vector3Int vec3Int = Vector3Int.RoundToInt(new Vector3(x,y,0));
                Vector3Int cellPos = _gridLayout.WorldToCell(new Vector3Int(vec3Int.x, vec3Int.y, 0));
                AllTiles.Add(cellPos); tbList.Add(TileArray[0]); 
            } 
        }
        cellPosArray = AllTiles.ToArray();
        tbArray = tbList.ToArray(); // SetTiles function is less resource heavy than SetTile one by one, thus creating arrays for SetTiles in this step

        FogTilemap.SetTiles(cellPosArray, tbArray);
    }

    public void DespawnAllFog(int aLevelStage)
    {
        if (aLevelStage == 3)
        {
            _lvl3 = null;

            Tilemap[] tilemapsArray = GameObject.FindObjectsOfType<Tilemap>();
            Tilemap tilemap = null;
            List<Vector3Int> AllTiles = new List<Vector3Int>();
            Vector3Int[] cellPosArray = new Vector3Int[] { };
            List<TileBase> tbList = new List<TileBase>();
            TileBase[] tbArray = new TileBase[] { };
            foreach (Tilemap tlmp in tilemapsArray)
            {
                if (tlmp.name == "Abisso")
                { tilemap = tlmp; }
            }

            // if I get lost for any reason, original grid cellcize was 1 with tiles = 32x32 pixels
            Grid grid = FindObjectOfType<Grid>();
            float scaleX = grid.cellSize.x; float scaleY = grid.cellSize.y;

            Bounds tlmpBounds = tilemap.GetComponent<TilemapRenderer>().bounds;
            float boundsXmin = tlmpBounds.min.x; float boundsXmax = tlmpBounds.max.x;
            float boundsYmin = tlmpBounds.min.y; float boundsYmax = tlmpBounds.max.y;

            // We are not adding absolute values, because tile center is in 0.5,0.5 coordinates for tile of scale 1.
            // Hence the formula of dividing by 2
            for (float x = (boundsXmin + (scaleX / 2)); x <= (boundsXmax - (scaleX / 2)); x += scaleX)
            {
                for (float y = (boundsYmin + (scaleY / 2)); y <= (boundsYmax - (scaleY / 2)); y += scaleY)
                {
                    Vector3Int vec3Int = Vector3Int.RoundToInt(new Vector3(x, y, 0));
                    Vector3Int cellPos = _gridLayout.WorldToCell(new Vector3Int(vec3Int.x, vec3Int.y, 0));
                    AllTiles.Add(cellPos); tbList.Add(null);
                }
            }
            cellPosArray = AllTiles.ToArray();
            tbArray = tbList.ToArray(); // SetTiles function is less resource heavy than SetTile one by one, thus creating arrays for SetTiles in this step

            FogTilemap.SetTiles(cellPosArray, tbArray);
        }
    }

    public void StopConcealingFog() { ReconcealTiles = false; }
}
