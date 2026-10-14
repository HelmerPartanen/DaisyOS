import QtQuick 2.15
import QtTest 1.3
import "../contents/ui/BentoLayout.js" as Bento

TestCase {
    name: "BentoLayout"

    function verifyBounds(rect) {
        verify(rect.x >= 0)
        verify(rect.y >= 0)
        verify(rect.width > 0)
        verify(rect.height > 0)
        verify(rect.x + rect.width <= 1.0001)
        verify(rect.y + rect.height <= 1.0001)
    }

    function test_counts_data() {
        return [
            { tag: "one", count: 1 },
            { tag: "two", count: 2 },
            { tag: "three", count: 3 },
            { tag: "four", count: 4 },
            { tag: "eight", count: 8 },
            { tag: "many", count: 17 }
        ]
    }

    function test_counts(data) {
        for (var i = 0; i < data.count; ++i) {
            verifyBounds(Bento.layout(data.count, i, 0, 16 / 9))
        }
    }

    function test_featuredWindowIsLargest() {
        var featured = Bento.layout(8, 5, 5, 16 / 9)
        var regular = Bento.layout(8, 0, 5, 16 / 9)
        verify(featured.width * featured.height > regular.width * regular.height)
    }

    function test_deterministic() {
        compare(JSON.stringify(Bento.layout(12, 7, 4, 16 / 9)),
                JSON.stringify(Bento.layout(12, 7, 4, 16 / 9)))
    }

    function test_aspectFit() {
        var fitted = Bento.fittedRect({ x: 0, y: 0, width: 1, height: 1 },
                                      1920, 1080, 1000, 700, 20, 38, 1)
        fuzzyCompare(fitted.width / fitted.height, 16 / 9, 0.01)
        verify(fitted.x >= 0)
        verify(fitted.y >= 0)
        verify(fitted.x + fitted.width <= 1000)
        verify(fitted.y + fitted.height + fitted.titleHeight <= 700)
    }

    function test_previewNeverExceedsOriginalWindow() {
        var fitted = Bento.fittedRect({ x: 0, y: 0, width: 1, height: 1 },
                                      640, 480, 2400, 1300, 20, 38, 1)
        verify(fitted.width <= 640)
        verify(fitted.height <= 480)
    }
}
