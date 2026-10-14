/****************************************************************************
** Meta object code from reading C++ file 'daisydecoration.h'
**
** Created by: The Qt Meta Object Compiler version 69 (Qt 6.11.2)
**
** WARNING! All changes made in this file will be lost!
*****************************************************************************/

#include "../../../daisydecoration.h"
#include <QtCore/qmetatype.h>

#include <QtCore/qtmochelpers.h>

#include <memory>


#include <QtCore/qxptype_traits.h>
#if !defined(Q_MOC_OUTPUT_REVISION)
#error "The header file 'daisydecoration.h' doesn't include <QObject>."
#elif Q_MOC_OUTPUT_REVISION != 69
#error "This file was generated using the moc from 6.11.2. It"
#error "cannot be used with the include files from this version of Qt."
#error "(The moc has changed too much.)"
#endif

#ifndef Q_CONSTINIT
#define Q_CONSTINIT
#endif

QT_WARNING_PUSH
QT_WARNING_DISABLE_DEPRECATED
QT_WARNING_DISABLE_GCC("-Wuseless-cast")
namespace {
struct qt_meta_tag_ZN18DaisyCaptionButtonE_t {};
} // unnamed namespace

template <> constexpr inline auto DaisyCaptionButton::qt_create_metaobjectdata<qt_meta_tag_ZN18DaisyCaptionButtonE_t>()
{
    namespace QMC = QtMocConstants;
    QtMocHelpers::StringRefStorage qt_stringData {
        "DaisyCaptionButton"
    };

    QtMocHelpers::UintData qt_methods {
    };
    QtMocHelpers::UintData qt_properties {
    };
    QtMocHelpers::UintData qt_enums {
    };
    return QtMocHelpers::metaObjectData<DaisyCaptionButton, qt_meta_tag_ZN18DaisyCaptionButtonE_t>(QMC::MetaObjectFlag{}, qt_stringData,
            qt_methods, qt_properties, qt_enums);
}
Q_CONSTINIT const QMetaObject DaisyCaptionButton::staticMetaObject = { {
    QMetaObject::SuperData::link<KDecoration3::DecorationButton::staticMetaObject>(),
    qt_staticMetaObjectStaticContent<qt_meta_tag_ZN18DaisyCaptionButtonE_t>.stringdata,
    qt_staticMetaObjectStaticContent<qt_meta_tag_ZN18DaisyCaptionButtonE_t>.data,
    qt_static_metacall,
    nullptr,
    qt_staticMetaObjectRelocatingContent<qt_meta_tag_ZN18DaisyCaptionButtonE_t>.metaTypes,
    nullptr
} };

void DaisyCaptionButton::qt_static_metacall(QObject *_o, QMetaObject::Call _c, int _id, void **_a)
{
    auto *_t = static_cast<DaisyCaptionButton *>(_o);
    (void)_t;
    (void)_c;
    (void)_id;
    (void)_a;
}

const QMetaObject *DaisyCaptionButton::metaObject() const
{
    return QObject::d_ptr->metaObject ? QObject::d_ptr->dynamicMetaObject() : &staticMetaObject;
}

void *DaisyCaptionButton::qt_metacast(const char *_clname)
{
    if (!_clname) return nullptr;
    if (!strcmp(_clname, qt_staticMetaObjectStaticContent<qt_meta_tag_ZN18DaisyCaptionButtonE_t>.strings))
        return static_cast<void*>(this);
    return KDecoration3::DecorationButton::qt_metacast(_clname);
}

int DaisyCaptionButton::qt_metacall(QMetaObject::Call _c, int _id, void **_a)
{
    _id = KDecoration3::DecorationButton::qt_metacall(_c, _id, _a);
    return _id;
}
namespace {
struct qt_meta_tag_ZN15DaisyDecorationE_t {};
} // unnamed namespace

template <> constexpr inline auto DaisyDecoration::qt_create_metaobjectdata<qt_meta_tag_ZN15DaisyDecorationE_t>()
{
    namespace QMC = QtMocConstants;
    QtMocHelpers::StringRefStorage qt_stringData {
        "DaisyDecoration",
        "reloadTheme",
        "",
        "updateLayout",
        "updateActiveState"
    };

    QtMocHelpers::UintData qt_methods {
        // Slot 'reloadTheme'
        QtMocHelpers::SlotData<void()>(1, 2, QMC::AccessPrivate, QMetaType::Void),
        // Slot 'updateLayout'
        QtMocHelpers::SlotData<void()>(3, 2, QMC::AccessPrivate, QMetaType::Void),
        // Slot 'updateActiveState'
        QtMocHelpers::SlotData<void()>(4, 2, QMC::AccessPrivate, QMetaType::Void),
    };
    QtMocHelpers::UintData qt_properties {
    };
    QtMocHelpers::UintData qt_enums {
    };
    return QtMocHelpers::metaObjectData<DaisyDecoration, qt_meta_tag_ZN15DaisyDecorationE_t>(QMC::MetaObjectFlag{}, qt_stringData,
            qt_methods, qt_properties, qt_enums);
}
Q_CONSTINIT const QMetaObject DaisyDecoration::staticMetaObject = { {
    QMetaObject::SuperData::link<KDecoration3::Decoration::staticMetaObject>(),
    qt_staticMetaObjectStaticContent<qt_meta_tag_ZN15DaisyDecorationE_t>.stringdata,
    qt_staticMetaObjectStaticContent<qt_meta_tag_ZN15DaisyDecorationE_t>.data,
    qt_static_metacall,
    nullptr,
    qt_staticMetaObjectRelocatingContent<qt_meta_tag_ZN15DaisyDecorationE_t>.metaTypes,
    nullptr
} };

void DaisyDecoration::qt_static_metacall(QObject *_o, QMetaObject::Call _c, int _id, void **_a)
{
    auto *_t = static_cast<DaisyDecoration *>(_o);
    if (_c == QMetaObject::InvokeMetaMethod) {
        switch (_id) {
        case 0: _t->reloadTheme(); break;
        case 1: _t->updateLayout(); break;
        case 2: _t->updateActiveState(); break;
        default: ;
        }
    }
    (void)_a;
}

const QMetaObject *DaisyDecoration::metaObject() const
{
    return QObject::d_ptr->metaObject ? QObject::d_ptr->dynamicMetaObject() : &staticMetaObject;
}

void *DaisyDecoration::qt_metacast(const char *_clname)
{
    if (!_clname) return nullptr;
    if (!strcmp(_clname, qt_staticMetaObjectStaticContent<qt_meta_tag_ZN15DaisyDecorationE_t>.strings))
        return static_cast<void*>(this);
    return KDecoration3::Decoration::qt_metacast(_clname);
}

int DaisyDecoration::qt_metacall(QMetaObject::Call _c, int _id, void **_a)
{
    _id = KDecoration3::Decoration::qt_metacall(_c, _id, _a);
    if (_id < 0)
        return _id;
    if (_c == QMetaObject::InvokeMetaMethod) {
        if (_id < 3)
            qt_static_metacall(this, _c, _id, _a);
        _id -= 3;
    }
    if (_c == QMetaObject::RegisterMethodArgumentMetaType) {
        if (_id < 3)
            *reinterpret_cast<QMetaType *>(_a[0]) = QMetaType();
        _id -= 3;
    }
    return _id;
}
QT_WARNING_POP
