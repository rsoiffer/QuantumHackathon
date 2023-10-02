using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class Level : MonoBehaviour
{
    [Header("General")] public LevelDefinition def;
    public int numRows = 2;

    [Header("Buckets")] public Transform bucketsParent;
    public Bucket bucketYesPrefab;
    public Bucket bucketNoPrefab;
    public Transform bucketTextParent;
    public TextMeshProUGUI bucketTextPrefab;

    [Header("Gate Slots")] public GridLayoutGroup commandGrid;
    public GameObject columnLabelPrefab;
    public GateSlot gateSlotPrefab;
    public string[] dimensionsAlphabet;
    public Color[] dimensionsColors;
    public GridLayoutGroup sourceGrid;

    [Header("Pegs")] public GridLayoutGroup pegGridParent;
    public GameObject pegEmptyPrefab;
    public Peg pegPrefab;

    [Header("States")] public State statePrefab;
    public float spawnRate = 1;
    public float timeScale = 1;

    [Header("UI Fixes")] public int updateAfterFrames = 2;
    public ScrollRect scrollRect;

    [Header("Victory")] public float victoryProgress;
    public float victoryThreshold = 100;
    public float wrongMultiplier = 100;

    private Gate[,] gateGrid;
    private Peg[,] pegGrid;
    private (int, int) prevScreenSize;
    private bool scrollRectChanged;
    private GateSlot[,] slotGrid;

    public int NumBits => def.numBits;

    public float VictoryPercent => Mathf.Clamp01(victoryProgress / victoryThreshold);

    private void Start()
    {
        slotGrid = new GateSlot[NumBits, numRows];
        pegGrid = new Peg[1 << NumBits, numRows];

        for (var state = 0; state < 1 << NumBits; state++)
        {
            var bucket = Instantiate(state == def.goalState ? bucketYesPrefab : bucketNoPrefab, bucketsParent);
            bucket.level = this;

            var bucketText = Instantiate(bucketTextPrefab, bucketTextParent);
            var stateChars = Convert.ToString(state, 2).PadLeft(NumBits, '0').ToCharArray();
            Array.Reverse(stateChars);
            var stateText = string.Join("",
                stateChars.Select((c, idx) => $"<color={ToRGBHex(dimensionsColors[idx])}>{c}</color>"));
            bucketText.text = $"|{stateText}}}";
        }

        commandGrid.constraintCount = NumBits;
        for (var dim = 0; dim < NumBits; dim++)
        {
            var columnLabel = Instantiate(columnLabelPrefab, commandGrid.transform);
            var tmp = columnLabel.GetComponentInChildren<TextMeshProUGUI>();
            tmp.text = $"<color={ToRGBHex(dimensionsColors[dim])}>{dimensionsAlphabet[dim]}</color>";
        }

        for (var row = 0; row < numRows; row++)
        for (var dim = 0; dim < NumBits; dim++)
        {
            var newGateSlot = Instantiate(gateSlotPrefab, commandGrid.transform);
            slotGrid[dim, row] = newGateSlot;
        }

        for (var i = 0; i < def.gatesBefore.Count; i++)
        {
            var gateSlot = commandGrid.transform.GetChild(i);
            gateSlot.GetComponent<GateSlot>().BlockDragging();
            if (def.gatesBefore[i] == null) continue;
            var newGate = Instantiate(def.gatesBefore[i], gateSlot);
            newGate.BlockDragging();
        }

        for (var i = 0; i < def.gatesAfter.Count; i++)
        {
            var gateSlot = commandGrid.transform.GetChild(commandGrid.transform.childCount - 1 - i);
            gateSlot.GetComponent<GateSlot>().BlockDragging();
            if (def.gatesAfter[i] == null) continue;
            var newGate = Instantiate(def.gatesAfter[i], gateSlot);
            newGate.BlockDragging();
        }

        foreach (var gate in def.gatesPlaceable) Instantiate(gate, sourceGrid.transform);

        pegGridParent.constraintCount = 1 << NumBits;
        for (var state = 0; state < 1 << NumBits; state++) Instantiate(pegEmptyPrefab, pegGridParent.transform);
        for (var row = 0; row < numRows; row++)
        for (var state = 0; state < 1 << NumBits; state++)
        {
            var newPeg = Instantiate(pegPrefab, pegGridParent.transform);
            pegGrid[state, row] = newPeg;
            newPeg.state = state;
            newPeg.row = row;
            newPeg.level = this;
        }

        StartCoroutine(SpawnCoroutine());

        scrollRect.onValueChanged.AddListener(_ => scrollRectChanged = true);
    }

    private void Update()
    {
        updateAfterFrames -= 1;

        var newGateGrid = new Gate[NumBits, numRows];
        for (var dim = 0; dim < NumBits; dim++)
        for (var row = 0; row < numRows; row++)
            newGateGrid[dim, row] = slotGrid[dim, row].GetComponentInChildren<Gate>();

        var newScreenSize = (Screen.width, Screen.height);

        if (SequenceEquals(newGateGrid, gateGrid) && newScreenSize == prevScreenSize && updateAfterFrames != 0 &&
            !scrollRectChanged) return;
        gateGrid = newGateGrid;
        prevScreenSize = newScreenSize;
        scrollRectChanged = false;

        for (var row = 0; row < numRows; row++)
        for (var state = 0; state < 1 << NumBits; state++)
            pegGrid[state, row].UpdateGraphics();
    }

    private IEnumerator SpawnCoroutine()
    {
        var spawnCooldown = 1f;
        while (true)
        {
            spawnCooldown -= Time.deltaTime * spawnRate;
            if (spawnCooldown < 0)
            {
                spawnCooldown = 1;
                var newState = Instantiate(statePrefab);
                newState.level = this;
                newState.ResetToState(def.startState);
            }

            yield return null;
        }
    }

    public void StateHitBottom(State state)
    {
        var fidelity = state.quballs.Sum(q => q.stateCurrent == def.goalState ? (float)q.Amplitude.Magnitude : 0);
        victoryProgress += fidelity - (1 - fidelity) * wrongMultiplier;
        victoryProgress = Mathf.Clamp(victoryProgress, 0, victoryThreshold);
    }

    public List<Gate> Gates(int row)
    {
        return Enumerable.Range(0, NumBits).Select(dim => gateGrid[dim, row]).ToList();
    }

    public Vector3 PegPos(int state, int row)
    {
        var mainCamera = Camera.main!;
        var slotPos = row < 0
            ? new Vector3(0, 5, 0)
            : row >= numRows
                ? new Vector3(0, -5, 0)
                : mainCamera.ScreenToWorldPoint(slotGrid[0, row].transform.position);
        var bucket = bucketsParent.GetChild(state);
        var bucketPos = mainCamera.ScreenToWorldPoint(bucket.position);
        return new Vector3(bucketPos.x, slotPos.y, 0);
    }

    private static bool SequenceEquals<T>(T[,] a, T[,] b) where T : Object
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Rank == b.Rank
               && Enumerable.Range(0, a.Rank).All(d => a.GetLength(d) == b.GetLength(d))
               && a.Cast<T>().Zip(b.Cast<T>(), (arg1, arg2) => arg1 == arg2).All(x => x);
    }

    public static string ToRGBHex(Color c)
    {
        return $"#{ToByte(c.r):X2}{ToByte(c.g):X2}{ToByte(c.b):X2}";
    }

    private static byte ToByte(float f)
    {
        f = Mathf.Clamp01(f);
        return (byte)(f * 255);
    }
}