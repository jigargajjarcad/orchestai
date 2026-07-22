-- Seed data for Phase 3 Sports Investigation MVP.
--
-- Case: Kawhi Leonard, San Antonio Spurs, 2017-18 NBA season.
-- Leonard missed the first 27 games of the season with right quadriceps
-- tendinopathy, returned Dec. 12, 2017 on a managed workload, and played
-- 9 games (Dec 12, 2017 - Jan 13, 2018) before being shut down indefinitely
-- on Jan. 17, 2018. Box-score minutes/points climb steadily across these
-- 9 games (16 min/13 pts -> 28 min/19 pts), i.e. the raw stats trend alone
-- reads as a successful ramp-up -- yet 4 days after the final seeded game
-- he was ruled out for the rest of the season. That is the deliberate
-- tension this dataset is for: DataAgent's own trend line will not predict
-- the shutdown; ResearchAgent's context does.
--
-- Box-score source (verified per-row against the individual ESPN box score
-- page for each game; see SourceUrl per row below; game IDs cross-checked
-- against the ESPN 2017-18 Spurs schedule at
-- https://www.espn.com/nba/team/schedule/_/name/sa/season/2018/seasontype/2
-- and against Kawhi Leonard's ESPN game log at
-- https://www.espn.com/nba/player/gamelog/_/id/6450/type/nba/year/2018).
--
-- Context source: Spurs announced Leonard "out indefinitely" on Jan. 17,
-- 2018 -- https://www.cbssports.com/nba/news/spurs-kawhi-leonard-out-indefinitely-to-continue-rehab-on-injured-quad
-- Corroborating detail on the post-Jan-13 soreness complaint and Popovich's
-- quotes -- https://www.espn.com/nba/story/_/id/23366667/inside-tension-kawhi-leonard-spurs
INSERT INTO "SportsPerformanceGames"
    ("AthleteName", "Sport", "GameNumber", "GameDate", "Opponent", "IsHomeGame", "MinutesPlayed", "Points", "ReportedInjuryNote", "SourceUrl")
VALUES
    ('Kawhi Leonard', 'Basketball', 1, '2017-12-12', 'Dallas Mavericks', false, 16, 13, NULL, 'https://www.espn.com/nba/boxscore/_/gameId/400975153'),
    ('Kawhi Leonard', 'Basketball', 2, '2017-12-15', 'Houston Rockets', false, 17, 12, NULL, 'https://www.espn.com/nba/boxscore/_/gameId/400975179'),
    ('Kawhi Leonard', 'Basketball', 3, '2017-12-18', 'LA Clippers', true, 16, 7, NULL, 'https://www.espn.com/nba/boxscore/_/gameId/400975200'),
    ('Kawhi Leonard', 'Basketball', 4, '2017-12-21', 'Utah Jazz', false, 20, 10, NULL, 'https://www.espn.com/nba/boxscore/_/gameId/400975221'),
    ('Kawhi Leonard', 'Basketball', 5, '2017-12-26', 'Brooklyn Nets', true, 26, 21, NULL, 'https://www.espn.com/nba/boxscore/_/gameId/400975248'),
    ('Kawhi Leonard', 'Basketball', 6, '2017-12-30', 'Detroit Pistons', false, 28, 18, NULL, 'https://www.espn.com/nba/boxscore/_/gameId/400975276'),
    ('Kawhi Leonard', 'Basketball', 7, '2018-01-02', 'New York Knicks', false, 31, 25, NULL, 'https://www.espn.com/nba/boxscore/_/gameId/400975296'),
    ('Kawhi Leonard', 'Basketball', 8, '2018-01-05', 'Phoenix Suns', true, 29, 21, NULL, 'https://www.espn.com/nba/boxscore/_/gameId/400975319'),
    ('Kawhi Leonard', 'Basketball', 9, '2018-01-13', 'Denver Nuggets', true, 28, 19, 'Leonard''s final game of the 2017-18 season: he reported renewed right quad soreness after this Denver win, and on Jan. 17, 2018 -- four days later -- the Spurs announced he would be sidelined indefinitely to continue quad rehab. He did not play again in the 2017-18 season.', 'https://www.espn.com/nba/boxscore/_/gameId/400975379');
