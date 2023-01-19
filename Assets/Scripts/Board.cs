using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

public class Board : MonoBehaviour
{
    public float timeBetweenPieces = 0.05f;

    public int width;
    public int height;
    public GameObject tileObject;

    public float cameraSizeOffset;
    public float caleraVerticalOffset;

    public int PointsPerMatch;

    public GameObject[] availablePieces;

    Tile[,] Tiles;
    Piece[,] Pieces;

    Tile startTile;
    Tile endTile;

    bool swappingPieces = false;

    // Start is called before the first frame update
    void Start()
    {
        Tiles = new Tile[width, height];
        Pieces = new Piece[width, height];

        SetupBoard();
        PositionCamera();

        if(GameManager.Instance.gameState == GameManager.GameState.InGame)
        {
            StartCoroutine(SetupPieces());
        }
        GameManager.Instance.OnGameStateUpdated.AddListener(GameStateUpdated);
    }

    private void OnDestroy()
    {
        GameManager.Instance.OnGameStateUpdated.RemoveListener(GameStateUpdated);
    }

    private void GameStateUpdated(GameManager.GameState newState)
    {
        if(newState == GameManager.GameState.InGame)
        {
            StartCoroutine(SetupPieces());
        }
        if(newState == GameManager.GameState.GameOver)
        {
            ClearAllPieces();
        }
    }

    private IEnumerator SetupPieces()
    {
        int maxIterations = 50;
        int currentIterations = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                yield return new WaitForSeconds(timeBetweenPieces);
                if(Pieces[x, y] == null){
                    currentIterations = 0;
                    var newPiece = CreatePieceAt(x, y);
                    while(HasPreviousMatches(x, y))
                    {
                        ClearPieceAt(x, y, false);
                        newPiece = CreatePieceAt(x, y);
                        currentIterations++;
                        if(currentIterations >= maxIterations)
                        {
                            break;
                        }
                    }
                }
            }
        }
        yield return null;
    }

    private void ClearPieceAt(int x, int y, bool animated)
    {
        var pieceToClear = Pieces[x, y];
        pieceToClear.Remove(animated);
        Pieces[x, y] = null;
    }

    private void ClearAllPieces()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if(Pieces[x, y] != null)
                {
                    ClearPieceAt(x, y, true);
                }
            }
        }
    }

    private Piece CreatePieceAt(int x, int y)
    {
        var selectedPiece = availablePieces[UnityEngine.Random.Range(0, availablePieces.Length)];
        var o = Instantiate(selectedPiece, new Vector3(x, y + 1, -5), Quaternion.identity);
        o.transform.parent = transform;
        Pieces[x,y] = o.GetComponent<Piece>();
        Pieces[x,y].Setup(x, y, this);
        Pieces[x,y].Move(x, y);
        return Pieces[x, y];
    }

    private void SetupBoard()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var o = Instantiate(tileObject, new Vector3(x, y, -5), Quaternion.identity);
                o.transform.parent = transform;
                Tiles[x, y] = o.GetComponent<Tile>();
                Tiles[x, y]?.Setup(x, y, this);
            }
        }
    }

    private void PositionCamera()
    {
        float newPosX = (float)width / 2f;
        float newPosY = (float)height / 2f;

        Camera.main.transform.position = new Vector3(newPosX - 0.5f, newPosY - 0.5f + caleraVerticalOffset, -10);

        float horizontal = width+1;
        float vertical = (height/2)+1;

        Camera.main.orthographicSize = Mathf.Max(horizontal + cameraSizeOffset, vertical + cameraSizeOffset);
    }

    public void TileDown(Tile tile_)
    {
        if(!swappingPieces && GameManager.Instance.gameState == GameManager.GameState.InGame){
            startTile = tile_;
        }
    }

    public void TileOver(Tile tile_)
    {
        if(!swappingPieces && GameManager.Instance.gameState == GameManager.GameState.InGame){
            endTile = tile_;
        }
    }

    public void TileUp(Tile tile_)
    {
        if(!swappingPieces && GameManager.Instance.gameState == GameManager.GameState.InGame) 
        {
            if(startTile!=null && endTile!=null && IsCloseTo(startTile, endTile))
            {
                StartCoroutine(SwapTiles());
            }
        }
    }

    IEnumerator SwapTiles()
    {
        swappingPieces = true;
        var StartPiece = Pieces[startTile.x, startTile.y];
        var EndPiece = Pieces[endTile.x, endTile.y];

        AudioManager.Instance.Move();

        StartPiece.Move(endTile.x, endTile.y);
        EndPiece.Move(startTile.x, startTile.y);

        Pieces[startTile.x, startTile.y] = EndPiece;
        Pieces[endTile.x, endTile.y] = StartPiece;

        yield return new WaitForSeconds(0.6f);

        var startMatches = GetMatchByPiece(startTile.x, startTile.y, 3);
        var endMatches = GetMatchByPiece(endTile.x, endTile.y, 3);

        var allMatches = startMatches.Union(endMatches).ToList();


        if(allMatches.Count==0)
        {
            AudioManager.Instance.Miss();
            StartPiece.Move(startTile.x, startTile.y);
            EndPiece.Move(endTile.x, endTile.y);

            Pieces[startTile.x, startTile.y] = StartPiece;
            Pieces[endTile.x, endTile.y] = EndPiece;
        } else
        {
            ClearPieces(allMatches);
            AwardPoints(allMatches);
        }

        startTile = null;
        endTile = null;
        swappingPieces = false;
    }

    private void ClearPieces(List<Piece> piecesToClear)
    {
        piecesToClear.ForEach(piece => {
            ClearPieceAt(piece.x, piece.y, true);
        });
        List<int> columns = GetColumns(piecesToClear);
        List<Piece> collapsedPieces = collapseColumns(columns, 0.3f);
        FindMatchesRecursively(collapsedPieces);
    }

    private void FindMatchesRecursively(List<Piece> collapsedPieces)
    {
        
        StartCoroutine(FindMatchesRecursivelyCoroutine(collapsedPieces));
    }

    IEnumerator FindMatchesRecursivelyCoroutine(List<Piece> collapsedPieces)
    {
        yield return new WaitForSeconds(1f);
        List<Piece> newMatches = new List<Piece>();
        collapsedPieces.ForEach(piece => {
            var matches = GetMatchByPiece(piece.x, piece.y, 3);
            if(matches != null)
            {
                newMatches = newMatches.Union(matches).ToList();
                ClearPieces(matches);
                AwardPoints(matches);
            }
        });
        if(newMatches.Count > 0)
        {
            var newCollapsedPieces = collapseColumns(GetColumns(newMatches), 0.3f);
            FindMatchesRecursively(newCollapsedPieces);
        } else
        {
            yield return new WaitForSeconds(0.1f);
            StartCoroutine(SetupPieces());
            swappingPieces = false;
        }
    }

    private List<Piece> collapseColumns(List<int> columns, float timeToCollapse)
    {
        List<Piece> movingPieces = new List<Piece>();

        for(int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            for(int j = 0; j < height; j++)
            {
                if(Pieces[column, j] == null)
                {
                    for(int jplus = j+1; jplus < height; jplus++)
                    {
                        if(Pieces[column, jplus] != null)
                        {
                            Pieces[column, jplus].Move(column, j);
                            Pieces[column, j] = Pieces[column, jplus];
                            if(!movingPieces.Contains(Pieces[column, j]))
                            {
                                movingPieces.Add(Pieces[column, j]);
                            }
                            Pieces[column, jplus] = null;
                            break;
                        }
                    }
                }
            }
        }
        return movingPieces;
    }

    private List<int> GetColumns(List<Piece> piecesToClear)
    {
        var result = new List<int>();

        piecesToClear.ForEach(piece => {
            if(!result.Contains(piece.x))
            {
                result.Add(piece.x);
            }
        });

        return result;
    }

    public bool IsCloseTo(Tile start, Tile end)
    {
        if(Math.Abs((start.x-end.x)) ==1 && start.y == end.y)
        {
            return true;
        } else if(Math.Abs((start.y-end.y)) ==1 && start.x == end.x)
        {
            return true;
        }
        return false;
    }

    private bool HasPreviousMatches(int xpos, int ypos)
    {
        var downMatches = GetMatchByDirection(xpos, ypos, new Vector2(0, -1), 2);
        var leftMatches = GetMatchByDirection(xpos, ypos, new Vector2(-1, 0), 2);

        if(downMatches==null) downMatches = new List<Piece>();
        if(leftMatches==null) leftMatches = new List<Piece>();

        return (downMatches.Count>0 || leftMatches.Count>0);
    }

    public List<Piece> GetMatchByDirection(int xpos, int ypos, Vector2 direction, int minPieces = 3)
    {
        List<Piece> matches = new List<Piece>();
        Piece startPiece = Pieces[xpos, ypos];
        matches.Add(startPiece);

        int nextX;
        int nextY;
        int maxVal = Math.Max(width, height);

        for(int i = 1; i<maxVal; i++)
        {
            nextX = xpos + ((int)direction.x * i);
            nextY = ypos + ((int)direction.y * i);
            if(nextX>=0 && nextX < width && nextY >= 0 && nextY < height)
            {
                var nextPiece = Pieces[nextX, nextY];
                if(nextPiece != null && nextPiece.pieceType == startPiece.pieceType)
                {
                    matches.Add(nextPiece);
                } 
                else
                {
                    break;
                }
            }
        }

        if(matches.Count >= minPieces)
        {
            return matches;
        }

        return null;
    }

    public List<Piece> GetMatchByPiece(int xpos, int ypos, int minPieces = 3)
    {
        var upMatchs = GetMatchByDirection(xpos, ypos, new Vector2(0, 1), 2);
        var downMatchs = GetMatchByDirection(xpos, ypos, new Vector2(0, -1), 2);
        var rightMatchs = GetMatchByDirection(xpos, ypos, new Vector2(1, 0), 2);
        var leftMatchs = GetMatchByDirection(xpos, ypos, new Vector2(-1, 0), 2);

        if(upMatchs == null) upMatchs = new List<Piece>();
        if(downMatchs == null) downMatchs = new List<Piece>();
        if(rightMatchs == null) rightMatchs = new List<Piece>();
        if(leftMatchs == null) leftMatchs = new List<Piece>();

        var verticalMatches = upMatchs.Union(downMatchs).ToList();
        var horizontalMatches = rightMatchs.Union(leftMatchs).ToList();

        var foundMatches = new List<Piece>();

        if(verticalMatches.Count >= minPieces)
        {
            foundMatches = foundMatches.Union(verticalMatches).ToList();
        }
        if(horizontalMatches.Count >= minPieces)
        {
            foundMatches = foundMatches.Union(horizontalMatches).ToList();
        }

        return foundMatches;
    }

    public void AwardPoints(List<Piece> allMatches)
    {
        GameManager.Instance.AddPoints(allMatches.Count * PointsPerMatch);
    }
}
