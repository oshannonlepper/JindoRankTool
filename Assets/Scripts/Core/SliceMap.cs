﻿using System;
using System.Collections.Generic;
using UnityEngine;

public enum Parity
{
    None,
    Forehand,
    Backhand,
    Reset
}

public struct PositioningData
{
    public float angle;
    public int x;
    public int y;
}

public struct BeatCutData
{
    public Parity sliceParity;
    public float sliceStartBeat;
    public float sliceEndBeat;
    public float swingEBPM;
    public PositioningData startPositioning;
    public PositioningData endPositioning;
    public List<ColourNote> notesInCut;
    public bool isReset;
    public bool isInverted;
}

// Adapted from Joshabi's ParityChecker
public class SliceMap
{
    // RIGHT HAND PARITY DICTIONARIES
    // Cut Direction -> Angle from Neutral (up down 0 degrees) given a Forehand Swing
    public static readonly Dictionary<int, float> rightForehandDict = new Dictionary<int, float>()
    { { 0, -180 }, { 1, 0 }, { 2, -90 }, { 3, 90 }, { 4, -135 }, { 5, 135 }, { 6, -45 }, { 7, 45 }, { 8, 0 } };
    // Cut Direction -> Angle from Neutral (up down 0 degrees) given a Backhand Swing
    public static readonly Dictionary<int, float> rightBackhandDict = new Dictionary<int, float>()
    { { 0, 0 }, { 1, -180 }, { 2, 90 }, { 3, -90 }, { 4, 45 }, { 5, -45 }, { 6, 135 }, { 7, -135 }, { 8, 0 } };

    // LEFT HAND PARITY DICTIONARIES
    // Cut Direction -> Angle from Neutral (up down 0 degrees) given a Forehand Swing
    public static readonly Dictionary<int, float> leftForehandDict = new Dictionary<int, float>()
    { { 0, -180 }, { 1, 0 }, { 2, 90 }, { 3, -90 }, { 4, 135 }, { 5, -135 }, { 6, 45 }, { 7, -45 }, { 8, 0 } };
    // Cut Direction -> Angle from Neutral (up down 0 degrees) given a Backhand Swing
    public static readonly Dictionary<int, float> leftBackhandDict = new Dictionary<int, float>()
    { { 0, 0 }, { 1, -180 }, { 2, -90 }, { 3, 90 }, { 4, -45 }, { 5, 45 }, { 6, -135 }, { 7, 135 }, { 8, 0 } };

    public static readonly Dictionary<int, int> opposingCutDict = new Dictionary<int, int>()
    { { 0, 1 }, { 1, 0 }, { 2, 3 }, { 3, 2 }, { 4, 7 }, { 7, 4 }, { 5, 6 }, { 6, 5 } };

    public static readonly List<int> forehandResetDict = new List<int>()
    { 1, 2, 3, 6, 7 };
    public static readonly List<int> backhandResetDict = new List<int>()
    { 0, 4, 5 };

    public static Dictionary<int, float> ForehandDict { get { return (_rightHand) ? rightForehandDict : leftForehandDict; } }
    public static Dictionary<int, float> BackhandDict { get { return (_rightHand) ? rightBackhandDict : leftBackhandDict; } }

    private IParityMethod _parityMethodology = new DefaultParityCheck();
    private List<ColourNote> _blocks;
    private List<BombNote> _bombs;
    private List<Obstacle> _walls;
    private List<BeatCutData> _cuts;
    private static bool _rightHand;
    private float _BPM = 0.0f;
    private int _playerXOffset = 0;
    private float _lastWallTime = 0;

    public int GetSliceCount()
    {
        return _cuts.Count;
    }

    public BeatCutData GetBeatCutData(int index)
    {
        return _cuts[index];
    }

    public SliceMap(float inBPM, List<ColourNote> blocks, List<BombNote> bombs, List<Obstacle> walls, bool isRightHand)
    {
        _BPM = inBPM;
        _blocks = new List<ColourNote>(blocks);
        _blocks.RemoveAll(x => isRightHand ? x.c == 0 : x.c == 1);
        _blocks.Sort((x, y) => x.b.CompareTo(y.b));
        _bombs = bombs;
        _bombs.Sort((x, y) => x.b.CompareTo(y.b));
        _walls = walls;
        _walls.Sort((x, y) => x.b.CompareTo(y.b));
        _cuts = GetCutData(_blocks, _bombs, walls, isRightHand);
    }

    List<BeatCutData> GetCutData(List<ColourNote> notes, List<BombNote> bombs, List<Obstacle> walls, bool isRightHand)
    {
        _rightHand = isRightHand;
        List<BeatCutData> result = new List<BeatCutData>();

        float sliderPrecision = 1 / 6f;
        List<ColourNote> notesInSwing = new List<ColourNote>();

        for (int i = 0; i < notes.Count - 1; i++)
        {
            ColourNote currentNote = notes[i];
            ColourNote nextNote = notes[i + 1];

            notesInSwing.Add(currentNote);

            // If precision falls under "Slider", or time stamp is the same, run
            // checks to figure out if it is a slider, window, stack ect..
            if (Mathf.Abs(currentNote.b - nextNote.b) <= sliderPrecision) {
                if (nextNote.d == 8 || notesInSwing[notesInSwing.Count-1].d == 8 ||
                    currentNote.d == nextNote.d || Mathf.Abs(ForehandDict[currentNote.d] - ForehandDict[nextNote.d]) <= 45)
                { continue; }
            }

            // Assume by default swinging forehanded
            BeatCutData sData = new BeatCutData();
            sData.notesInCut = new List<ColourNote>(notesInSwing);
            sData.sliceParity = Parity.Forehand;
            sData.sliceStartBeat = notesInSwing[0].b;
            sData.sliceEndBeat = notesInSwing[notesInSwing.Count - 1].b + 0.1f;
            sData.startPositioning.angle = ForehandDict[notesInSwing[0].d];
            sData.startPositioning.x = notesInSwing[0].x;
            sData.startPositioning.y = notesInSwing[0].y;
            sData.endPositioning.angle = ForehandDict[notesInSwing[notesInSwing.Count-1].d];
            sData.endPositioning.x = notesInSwing[notesInSwing.Count-1].x;
            sData.endPositioning.y = notesInSwing[notesInSwing.Count-1].y;

            // If first swing, figure out starting orientation based on cut direction
            if (result.Count == 0) {
                if (currentNote.d == 0 || currentNote.d == 4 || currentNote.d == 5) {
                    sData.sliceParity = Parity.Backhand;
                    sData.startPositioning.angle = BackhandDict[notesInSwing[0].d];
                    sData.endPositioning.angle = BackhandDict[notesInSwing[notesInSwing.Count - 1].d]; 
                }
                result.Add(sData);
                notesInSwing.Clear();
                continue;
            }

            // If previous swing exists
            BeatCutData lastSwing = result[result.Count - 1];
            ColourNote lastNote = lastSwing.notesInCut[lastSwing.notesInCut.Count - 1];

            // Performs Dot Checks under the assumption is a forehand swing
            sData = DotChecks(sData, lastSwing);

            // Get swing EBPM, if reset then double
            sData.swingEBPM = SwingEBPM(_BPM, currentNote.b - lastNote.b);
            if (sData.isReset) { sData.swingEBPM *= 2; }

            // Invert Check
            if (sData.isInverted == false)
            {
                for (int last = 0; last < lastSwing.notesInCut.Count; last++)
                {
                    for (int next = 0; next < notesInSwing.Count; next++)
                    {
                        if (IsInvert(lastSwing.notesInCut[last], notesInSwing[next]))
                        {
                            sData.isInverted = true;
                            break;
                        }
                    }
                }
            }

            // Work out current player XOffset for bomb calculations
            List<Obstacle> wallsInBetween = walls.FindAll(x => x.b > lastNote.b && x.b < notesInSwing[notesInSwing.Count - 1].b);
            if(wallsInBetween.Count != 0) {
                foreach(Obstacle wall in wallsInBetween)
                {
                    if (wall.x == 1 || wall.x == 0 && wall.w > 1) {
                        _playerXOffset = 1;
                        _lastWallTime = wall.b;
                    } else if (wall.x == 2) {
                        _playerXOffset = -1;
                        _lastWallTime = wall.b;
                    }
                }
            }

            // If time since dodged exceeds a set amount in seconds, undo dodge
            var undodgeCheckTime = 0.35f;
            if (BeatToSeconds(_BPM, notesInSwing[notesInSwing.Count-1].b - _lastWallTime) > undodgeCheckTime) { _playerXOffset = 0; }

            // Work out Parity
            List<BombNote> bombsBetweenSwings = bombs.FindAll(x => x.b > lastNote.b && x.b < notesInSwing[notesInSwing.Count - 1].b);
            sData.sliceParity = _parityMethodology.ParityCheck(lastSwing, notesInSwing[0], bombsBetweenSwings, _playerXOffset, _rightHand);

            // If backhand, readjust start and end angle
            if (sData.sliceParity == Parity.Backhand) {
                sData.startPositioning.angle = BackhandDict[notesInSwing[0].d];
                sData.endPositioning.angle = BackhandDict[notesInSwing[notesInSwing.Count - 1].d];
                sData = DotChecks(sData, result[result.Count - 1]);
            }

            if (sData.sliceParity == lastSwing.sliceParity) { sData.isReset = true; }

            // Add swing to list
            result.Add(sData);
            notesInSwing.Clear();
        }
        result = FixDotOrientation(result);
        result = AddBombResetAvoidance(result);
        return result;
    }

    #region Dots and Bombs Checks
    // Modifies a Swing if Dot Notes are involved
    public BeatCutData DotChecks(BeatCutData curSwing, BeatCutData lastSwing)
    {
        // If the start and end notes are dots
        if(curSwing.notesInCut[0].d == 8 && curSwing.notesInCut[curSwing.notesInCut.Count-1].d == 8)
        {
            // If there is more then 1 note, indicating a dot stack, tower, or zebra slider
            if (curSwing.notesInCut.Count > 1) {
                // Check which note is closer from last swing
                // Depending on which is closer, calculate angle
                Vector2 lastSwingVec = LevelUtils.GetWorldXYFromBeatmapCoords(lastSwing.notesInCut[0].x, lastSwing.notesInCut[0].y);
                Vector2 firstNoteVec = LevelUtils.GetWorldXYFromBeatmapCoords(curSwing.notesInCut[0].x, curSwing.notesInCut[0].y);
                Vector2 lastNoteVec = LevelUtils.GetWorldXYFromBeatmapCoords(curSwing.notesInCut[curSwing.notesInCut.Count-1].x, curSwing.notesInCut[curSwing.notesInCut.Count - 1].y);

                float distanceToStart = Vector2.Distance(firstNoteVec, lastSwingVec);
                float distanceToEnd = Vector2.Distance(lastNoteVec, lastSwingVec);

                // Depending on Parity, calculate the cut direction of the dot stack
                curSwing.startPositioning.angle = (curSwing.sliceParity == Parity.Forehand) ?
                    AngleBetweenNotes(curSwing.notesInCut[curSwing.notesInCut.Count - 1], curSwing.notesInCut[0]) :
                    AngleBetweenNotes(curSwing.notesInCut[0], curSwing.notesInCut[curSwing.notesInCut.Count - 1]);

                if (distanceToStart > distanceToEnd) {
                    curSwing.notesInCut.Reverse();
                    curSwing.startPositioning.angle = (curSwing.sliceParity == Parity.Forehand) ?
                        AngleBetweenNotes(curSwing.notesInCut[curSwing.notesInCut.Count - 1], curSwing.notesInCut[0]) :
                        AngleBetweenNotes(curSwing.notesInCut[0], curSwing.notesInCut[curSwing.notesInCut.Count - 1]);
                    curSwing.sliceStartBeat = curSwing.notesInCut[0].b;
                    curSwing.sliceEndBeat = curSwing.notesInCut[curSwing.notesInCut.Count - 1].b + 0.1f;
                    curSwing.startPositioning.x = curSwing.notesInCut[0].x;
                    curSwing.startPositioning.y = curSwing.notesInCut[0].y;
                    curSwing.endPositioning.x = curSwing.notesInCut[curSwing.notesInCut.Count - 1].x;
                    curSwing.endPositioning.y = curSwing.notesInCut[curSwing.notesInCut.Count - 1].y;
                }

                // Set ending angle equal to starting angle
                curSwing.endPositioning.angle = curSwing.startPositioning.angle;

            } else {

                // If its only a single note and its a dot, cut it in the opposing direction to the last swing (assuming that wasnt a dot)
                if (lastSwing.notesInCut[lastSwing.notesInCut.Count - 1].d != 8)
                {
                    curSwing.startPositioning.angle = AngleGivenCutDirection(opposingCutDict[lastSwing.notesInCut[lastSwing.notesInCut.Count - 1].d], curSwing.sliceParity);
                    curSwing.endPositioning.angle = curSwing.startPositioning.angle;
                }
            }
        } else if (curSwing.notesInCut[0].d == 8 && curSwing.notesInCut[curSwing.notesInCut.Count-1].d != 8) {
            // In the event its a dot then an arrow
            curSwing.startPositioning.angle = AngleGivenCutDirection(curSwing.notesInCut[curSwing.notesInCut.Count - 1].d, curSwing.sliceParity);
            curSwing.endPositioning.angle = (curSwing.sliceParity == Parity.Forehand) ?
                ForehandDict[curSwing.notesInCut[curSwing.notesInCut.Count - 1].d] :
                BackhandDict[curSwing.notesInCut[curSwing.notesInCut.Count - 1].d];
        }
        return curSwing;
    }
    // Attempts to fix the orientation in which a dot note is swung based on prior and post dot swings
    private List<BeatCutData> FixDotOrientation(List<BeatCutData> swings)
    {
        // For each note
        for (int i = 0; i < swings.Count - 1; i++)
        {
            // If the swing involves a singular dot note
            if (swings[i].notesInCut[0].d == 8 && swings[i].notesInCut.Count == 1)
            {

                // Get the previous swing and default the next swing
                if (i - 1 < 0) continue;
                BeatCutData previousSwing = swings[i - 1];
                BeatCutData postDotSwing = swings[i + 1];

                // If the previous swing is also a dot, just skip to the next note so calculations arent repeated
                if (previousSwing.notesInCut[0].d == 8) { continue; }

                // Currently 1 dot, loop through notes until we find a non-dot note
                int dotsLength = 1;
                for (int j = i; j < swings.Count - 1; j++)
                {
                    if (swings[j].notesInCut[0].d == 8 && swings[j].notesInCut[swings[j].notesInCut.Count - 1].d == 8 && swings[j].notesInCut.Count > 1)
                    {
                        postDotSwing = previousSwing;
                        break;
                    }

                    if (swings[j].notesInCut[0].d != 8 || swings[j].notesInCut[swings[j].notesInCut.Count - 1].d != 8)
                    {
                        // Get a reference to the non-dot swing and break
                        postDotSwing = swings[j];
                        break;
                    }
                    dotsLength++;
                }

                // If the dots go on for more then 3 consecutive hits
                if (dotsLength > 2)
                {
                    if (_rightHand) postDotSwing.startPositioning.angle = Mathf.Clamp(postDotSwing.startPositioning.angle, -90, 0);
                    if (!_rightHand) postDotSwing.startPositioning.angle = Mathf.Clamp(postDotSwing.startPositioning.angle, 0, 90);
                    postDotSwing.endPositioning.angle = postDotSwing.startPositioning.angle;
                }

                // For every dot note, lerp the swing rotation
                for (int k = 0; k < dotsLength; k++)
                {
                    BeatCutData swingToModify = swings[k + i];
                    swingToModify.startPositioning.angle = Mathf.Lerp(previousSwing.startPositioning.angle, postDotSwing.startPositioning.angle, dotsLength);
                    swingToModify.startPositioning.angle = Mathf.Clamp(swingToModify.startPositioning.angle, -90, 90);
                    swingToModify.endPositioning.angle = swingToModify.startPositioning.angle;
                    swings[k + i] = swingToModify;
                }
            }
        }
        return swings;
    }
    // Attempts to add bomb avoidance based on the isReset tag for a list of swings.
    // NOTE: To improve this, probably want bomb detection in its own function and these swings
    // would be added for each bomb in the sabers path rather then only for bomb resets.
    private List<BeatCutData> AddBombResetAvoidance(List<BeatCutData> swings)
    {
        List<BeatCutData> result = new List<BeatCutData>(swings);
        int swingsAdded = 0;

        for (int i = 0; i < swings.Count - 1; i++)
        {
            if (swings[i].isReset)
            {
                // Reference to last swing
                ColourNote lastHitNote = swings[i - 1].notesInCut[swings[i - 1].notesInCut.Count - 1];

                // Create a new swing with inverse parity to the last.
                BeatCutData emptySwing = new BeatCutData();
                emptySwing.sliceParity = (swings[i].sliceParity == Parity.Forehand) ? Parity.Backhand : Parity.Forehand;
                emptySwing.sliceStartBeat = swings[i - 1].sliceEndBeat + SecondsToBeats(_BPM, 0.1f);
                emptySwing.sliceEndBeat = emptySwing.sliceStartBeat + 0.2f;
                emptySwing.startPositioning.x = lastHitNote.x;
                emptySwing.startPositioning.y = lastHitNote.y;

                // If the last hit was a dot, pick the opposing direction based on parity.
                if (lastHitNote.d == 8)
                {
                    emptySwing.startPositioning.angle = (emptySwing.sliceParity == Parity.Forehand) ?
                        ForehandDict[1] : BackhandDict[0];
                }
                else
                {
                    // If the last hit was arrowed, figure out the opposing cut direction and use that.
                    emptySwing.startPositioning.angle = (emptySwing.sliceParity == Parity.Forehand) ?
                        ForehandDict[opposingCutDict[lastHitNote.d]] :
                        BackhandDict[opposingCutDict[lastHitNote.d]];
                }

                // End angle should be the same as the start angle
                emptySwing.endPositioning.angle = emptySwing.startPositioning.angle;

                // Calculate the direction, set it, then insert this swing into the returned result list.
                Vector3 dir = Quaternion.Euler(0, 0, emptySwing.startPositioning.angle) * Vector3.up;
                Vector2 endPosition = new Vector2(dir.x * 2f, dir.y * 2f);
                emptySwing.endPositioning.x = (int)endPosition.x;
                emptySwing.endPositioning.y = (int)endPosition.y;
                result.Insert(i + swingsAdded, emptySwing);
                swingsAdded++;
            }
        }
        return result;
    }
    #endregion

    #region Helper Functions
    // Given a cut direction ID, return angle from appropriate dictionary
    private float AngleGivenCutDirection(int cutDirection, Parity parity)
    {
        return (parity == Parity.Forehand) ? ForehandDict[cutDirection] : BackhandDict[cutDirection];
    }
    // Returns the angle between any 2 given notes
    private float AngleBetweenNotes(ColourNote firstNote, ColourNote lastNote) {
        Vector3 firstNoteCoords = new Vector3(firstNote.x, firstNote.y, 0);
        Vector3 lastNoteCoords = new Vector3 (lastNote.x, lastNote.y, 0);
        float angle = Vector3.SignedAngle(Vector3.up, lastNoteCoords - firstNoteCoords, Vector3.forward);
        if (!_rightHand && Mathf.Abs(angle) > 0) angle *= -1;
        return angle;
    }
    // Determines if a Note is inverted
    private bool IsInvert(ColourNote lastNote, ColourNote nextNote)
    {
        // Is Note B in the direction of Note A's cutDirection.
        switch (lastNote.d)
        {
            case 0:
                // Up note
                return (nextNote.x > lastNote.x);
            case 1:
                // Down note
                return (nextNote.x < lastNote.x);
            case 2:
                // Left note
                return (nextNote.x < lastNote.x);
            case 3:
                // Right note
                return (nextNote.y > lastNote.y);
            case 4:
                // Up, Left note
                return (nextNote.x < lastNote.x && nextNote.y > lastNote.y);
            case 5:
                // Up, Right note
                return (nextNote.x > lastNote.x && nextNote.y > lastNote.y);
            case 6:
                // Down, Left note
                return (nextNote.x < lastNote.x && nextNote.y < lastNote.y);
            case 7:
                // Down, Right note
                return (nextNote.x > lastNote.x && nextNote.y < lastNote.y);
        }
        return false;
    }
    // Calculates the effective BPM of a swing
    private float SwingEBPM(float BPM, float beatDiff)
    {
        var seconds = BeatToSeconds(BPM, beatDiff);
        TimeSpan time = TimeSpan.FromSeconds(seconds);

        return (float)((60000 / time.TotalMilliseconds) / 2);
    }
    // Converts beats to seconds based on BPM
    public float BeatToSeconds(float BPM, float beatDiff)
    {
        return (beatDiff / (BPM / 60));
    }
    // Converts seconds to beats based on BPM
    public float SecondsToBeats(float BPM, float seconds)
    {
        return (BPM/60) * seconds;
    }
    #endregion
}